using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 检测到的 CLI 配置
/// </summary>
public class DetectedCliConfig
{
    /// <summary>
    /// 工具 ID (claude-code, codex, opencode)
    /// </summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// 配置来源
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 检测到的环境变量
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// 配置文件路径（如果有）
    /// </summary>
    public string? ConfigFilePath { get; set; }

    /// <summary>
    /// 是否有有效的 API 密钥
    /// </summary>
    public bool HasValidApiKey { get; set; }

    /// <summary>
    /// 是否从配置文件导入
    /// </summary>
    public bool FromConfigFile { get; set; }

    /// <summary>
    /// 是否从系统环境变量导入
    /// </summary>
    public bool FromSystemEnv { get; set; }
}

/// <summary>
/// 本地 CLI 配置检测结果
/// </summary>
public class LocalCliConfigDetectionResult
{
    /// <summary>
    /// 所有检测到的配置
    /// </summary>
    public List<DetectedCliConfig> DetectedConfigs { get; set; } = new();

    /// <summary>
    /// 是否检测到任何配置
    /// </summary>
    public bool HasAnyConfig => DetectedConfigs.Any(c => c.EnvironmentVariables.Any());
}

/// <summary>
/// 本地 CLI 配置检测服务接口
/// </summary>
public interface ILocalCliConfigDetector
{
    /// <summary>
    /// 检测本地 CLI 配置
    /// </summary>
    Task<LocalCliConfigDetectionResult> DetectLocalConfigsAsync();
}

/// <summary>
/// 本地 CLI 配置检测服务
/// 检测本地已安装的 CLI 工具配置文件和环境变量
/// </summary>
public class LocalCliConfigDetector : ILocalCliConfigDetector
{
    private readonly ILogger<LocalCliConfigDetector> _logger;

    // Claude Code 配置目录和环境变量
    private static readonly string[] ClaudeConfigPaths = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "config.json"),
    };

    private static readonly string[] ClaudeEnvVarKeys = new[]
    {
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_BASE_URL",
        "ANTHROPIC_MODEL",
        "ANTHROPIC_SMALL_FAST_MODEL",
        "CLAUDE_CODE_USE_BEDROCK",
        "CLAUDE_CODE_USE_VERTEX",
        "CLAUDE_CODE_SKIP_PERMISSIONS",
        "CLAUDE_CODE_MAX_TOKENS",
        "CLAUDE_CODE_TEMPERATURE",
    };

    // Codex 配置路径和环境变量
    private static readonly string[] CodexConfigPaths = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "instructions.md"),
    };

    private static readonly string[] CodexEnvVarKeys = new[]
    {
        "OPENAI_API_KEY",
        "NEW_API_KEY",
        "CODEX_BASE_URL",
        "CODEX_MODEL",
        "CODEX_MODEL_PROVIDER",
        "CODEX_MAX_CONTEXT",
        "CODEX_CONTEXT_COMPACT_LIMIT",
        "CODEX_PROFILE",
        "CODEX_PROVIDER_NAME",
        "CODEX_WIRE_API",
        "CODEX_APPROVAL_POLICY",
        "CODEX_MODEL_REASONING_EFFORT",
        "CODEX_MODEL_REASONING_SUMMARY",
        "CODEX_MODEL_VERBOSITY",
        "CODEX_SANDBOX_MODE",
    };

    // OpenCode 配置路径和环境变量
    private static readonly string[] OpenCodeConfigPaths = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode", "config.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opencode", "config.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "opencode", "config.json"),
    };

    private static readonly string[] OpenCodeEnvVarKeys = new[]
    {
        "OPENCODE_CONFIG_CONTENT",
        "OPENCODE_CONFIG",
        "OPENCODE_CONFIG_DIR",
        "OPENCODE_AUTO_SHARE",
        "OPENCODE_GIT_BASH_PATH",
        "OPENCODE_PERMISSION",
        "OPENCODE_CLIENT",
        "OPENCODE_ENABLE_EXA",
    };

    public LocalCliConfigDetector(ILogger<LocalCliConfigDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 检测本地 CLI 配置
    /// </summary>
    public async Task<LocalCliConfigDetectionResult> DetectLocalConfigsAsync()
    {
        var result = new LocalCliConfigDetectionResult();

        try
        {
            // 检测 Claude Code 配置
            var claudeConfig = await DetectClaudeCodeConfigAsync();
            if (claudeConfig != null)
            {
                result.DetectedConfigs.Add(claudeConfig);
            }

            // 检测 Codex 配置
            var codexConfig = await DetectCodexConfigAsync();
            if (codexConfig != null)
            {
                result.DetectedConfigs.Add(codexConfig);
            }

            // 检测 OpenCode 配置
            var openCodeConfig = await DetectOpenCodeConfigAsync();
            if (openCodeConfig != null)
            {
                result.DetectedConfigs.Add(openCodeConfig);
            }

            _logger.LogInformation("本地配置检测完成，共检测到 {Count} 个工具配置", result.DetectedConfigs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检测本地 CLI 配置失败");
        }

        return result;
    }

    /// <summary>
    /// 检测 Claude Code 配置
    /// </summary>
    private async Task<DetectedCliConfig?> DetectClaudeCodeConfigAsync()
    {
        var config = new DetectedCliConfig
        {
            ToolId = "claude-code",
            ToolName = "Claude Code",
            Source = "本地配置"
        };

        // 检测配置文件
        foreach (var configPath in ClaudeConfigPaths)
        {
            if (File.Exists(configPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(configPath);
                    var json = JsonDocument.Parse(content);

                    // 读取配置文件中的值
                    if (json.RootElement.TryGetProperty("apiKey", out var apiKeyElement))
                    {
                        var apiKey = apiKeyElement.GetString();
                        if (!string.IsNullOrWhiteSpace(apiKey))
                        {
                            config.EnvironmentVariables["ANTHROPIC_API_KEY"] = apiKey;
                            config.HasValidApiKey = true;
                        }
                    }

                    if (json.RootElement.TryGetProperty("baseUrl", out var baseUrlElement))
                    {
                        var baseUrl = baseUrlElement.GetString();
                        if (!string.IsNullOrWhiteSpace(baseUrl))
                        {
                            config.EnvironmentVariables["ANTHROPIC_BASE_URL"] = baseUrl;
                        }
                    }

                    if (json.RootElement.TryGetProperty("model", out var modelElement))
                    {
                        var model = modelElement.GetString();
                        if (!string.IsNullOrWhiteSpace(model))
                        {
                            config.EnvironmentVariables["ANTHROPIC_MODEL"] = model;
                        }
                    }

                    config.ConfigFilePath = configPath;
                    _logger.LogInformation("检测到 Claude Code 配置文件: {Path}", configPath);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "解析 Claude Code 配置文件失败: {Path}", configPath);
                }
            }
        }

        // 检测环境变量
        foreach (var envKey in ClaudeEnvVarKeys)
        {
            var value = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                // 如果是 API 密钥，检查是否有效
                if (envKey == "ANTHROPIC_API_KEY")
                {
                    config.HasValidApiKey = IsValidApiKey(value);
                }

                // 不覆盖配置文件中的值
                if (!config.EnvironmentVariables.ContainsKey(envKey))
                {
                    config.EnvironmentVariables[envKey] = value;
                    _logger.LogDebug("检测到 Claude Code 环境变量: {Key}", envKey);
                }
            }
        }

        return config.EnvironmentVariables.Any() ? config : null;
    }

    /// <summary>
    /// 检测 Codex 配置
    /// </summary>
    private async Task<DetectedCliConfig?> DetectCodexConfigAsync()
    {
        var config = new DetectedCliConfig
        {
            ToolId = "codex",
            ToolName = "Codex",
            Source = "本地配置"
        };

        // 检测配置文件
        foreach (var configPath in CodexConfigPaths)
        {
            if (File.Exists(configPath))
            {
                config.ConfigFilePath = configPath;
                _logger.LogInformation("检测到 Codex 配置文件: {Path}", configPath);

                // 尝试解析 JSON 配置（如果有）
                if (configPath.EndsWith(".json"))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(configPath);
                        var json = JsonDocument.Parse(content);

                        // Codex 配置结构可能不同，这里只做基本解析
                        if (json.RootElement.TryGetProperty("apiKey", out var apiKeyElement))
                        {
                            var apiKey = apiKeyElement.GetString();
                            if (!string.IsNullOrWhiteSpace(apiKey))
                            {
                                config.EnvironmentVariables["NEW_API_KEY"] = apiKey;
                                config.HasValidApiKey = true;
                            }
                        }
                    }
                    catch
                    {
                        // 忽略解析错误
                    }
                }
                break;
            }
        }

        // 检测环境变量
        foreach (var envKey in CodexEnvVarKeys)
        {
            var value = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (envKey is "OPENAI_API_KEY" or "NEW_API_KEY")
                {
                    config.HasValidApiKey = IsValidApiKey(value);
                }

                if (!config.EnvironmentVariables.ContainsKey(envKey))
                {
                    config.EnvironmentVariables[envKey] = value;
                    _logger.LogDebug("检测到 Codex 环境变量: {Key}", envKey);
                }
            }
        }

        return config.EnvironmentVariables.Any() ? config : null;
    }

    /// <summary>
    /// 检测 OpenCode 配置
    /// </summary>
    private async Task<DetectedCliConfig?> DetectOpenCodeConfigAsync()
    {
        var config = new DetectedCliConfig
        {
            ToolId = "opencode",
            ToolName = "OpenCode",
            Source = "本地配置"
        };

        // 检测配置文件
        foreach (var configPath in OpenCodeConfigPaths)
        {
            if (File.Exists(configPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(configPath);
                    config.ConfigFilePath = configPath;

                    // 将配置文件内容作为 OPENCODE_CONFIG_CONTENT
                    config.EnvironmentVariables["OPENCODE_CONFIG_CONTENT"] = content;

                    // 尝试解析 JSON 检查是否有 API 密钥配置
                    try
                    {
                        var json = JsonDocument.Parse(content);
                        if (json.RootElement.TryGetProperty("providers", out var providers) ||
                            json.RootElement.TryGetProperty("model", out _))
                        {
                            config.HasValidApiKey = true;
                        }
                    }
                    catch
                    {
                        // 忽略解析错误
                    }

                    _logger.LogInformation("检测到 OpenCode 配置文件: {Path}", configPath);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "读取 OpenCode 配置文件失败: {Path}", configPath);
                }
            }
        }

        // 检测环境变量
        foreach (var envKey in OpenCodeEnvVarKeys)
        {
            var value = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (!config.EnvironmentVariables.ContainsKey(envKey))
                {
                    config.EnvironmentVariables[envKey] = value;
                    _logger.LogDebug("检测到 OpenCode 环境变量: {Key}", envKey);
                }
            }
        }

        return config.EnvironmentVariables.Any() ? config : null;
    }

    /// <summary>
    /// 验证 API 密钥格式
    /// </summary>
    private static bool IsValidApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        // Anthropic API 密钥通常以 sk-ant- 开头
        if (apiKey.StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase))
            return true;

        // OpenAI API 密钥通常以 sk- 开头
        if (apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) && apiKey.Length > 20)
            return true;

        // 其他格式的密钥（至少 20 个字符）
        return apiKey.Length >= 20;
    }
}
