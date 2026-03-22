using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Repositories.Base.CliToolEnv;
using WebCodeCli.Domain.Repositories.Base.UserCliToolEnv;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// CLI 工具环境变量服务接口
/// </summary>
public interface ICliToolEnvironmentService
{
    /// <summary>
    /// 获取指定工具的环境变量配置（优先从数据库读取,否则从appsettings读取）
    /// </summary>
    Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string toolId, string? username = null);

    /// <summary>
    /// 保存指定工具的环境变量配置到数据库
    /// </summary>
    Task<bool> SaveEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars, string? username = null);

    /// <summary>
    /// 删除指定工具的环境变量配置
    /// </summary>
    Task<bool> DeleteEnvironmentVariablesAsync(string toolId, string? username = null);

    /// <summary>
    /// 重置为appsettings中的默认配置
    /// </summary>
    Task<Dictionary<string, string>> ResetToDefaultAsync(string toolId, string? username = null);
}

/// <summary>
/// CLI 工具环境变量服务实现
/// </summary>
[ServiceDescription(typeof(ICliToolEnvironmentService), ServiceLifetime.Scoped)]
public class CliToolEnvironmentService : ICliToolEnvironmentService
{
    private readonly ILogger<CliToolEnvironmentService> _logger;
    private readonly CliToolsOption _options;
    private readonly ICliToolEnvironmentVariableRepository _repository;
    private readonly IUserCliToolEnvironmentVariableRepository _userRepository;
    private readonly IUserContextService _userContextService;

    public CliToolEnvironmentService(
        ILogger<CliToolEnvironmentService> logger,
        IOptions<CliToolsOption> options,
        ICliToolEnvironmentVariableRepository repository,
        IUserCliToolEnvironmentVariableRepository userRepository,
        IUserContextService userContextService)
    {
        _logger = logger;
        _options = options.Value;
        _repository = repository;
        _userRepository = userRepository;
        _userContextService = userContextService;
    }

    /// <summary>
    /// 获取指定工具的环境变量配置（优先从数据库读取,否则从appsettings读取）
    /// </summary>
    public async Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string toolId, string? username = null)
    {
        try
        {
            var result = await GetInheritedEnvironmentVariablesAsync(toolId);

            var resolvedUsername = ResolveUsername(username);
            if (!string.IsNullOrWhiteSpace(resolvedUsername))
            {
                var userEnvVars = await _userRepository.GetEnvironmentVariablesAsync(resolvedUsername, toolId);
                ApplyOverrides(result, userEnvVars);
            }

            _logger.LogInformation("获取工具 {ToolId} 的环境变量配置，用户={Username}，最终 {Count} 个", toolId, resolvedUsername, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取工具 {ToolId} 的环境变量失败", toolId);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 保存指定工具的环境变量配置到数据库
    /// </summary>
    public async Task<bool> SaveEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars, string? username = null)
    {
        try
        {
            var resolvedUsername = ResolveUsername(username);
            if (string.IsNullOrWhiteSpace(resolvedUsername))
            {
                return false;
            }

            var normalizedEnvVars = NormalizeEnvVars(envVars, keepEmptyValues: false);
            var inheritedEnvVars = await GetInheritedEnvironmentVariablesAsync(toolId);
            var persistedEnvVars = BuildPersistedUserEnvVars(normalizedEnvVars, inheritedEnvVars);

            var result = await _userRepository.SaveEnvironmentVariablesAsync(resolvedUsername, toolId, persistedEnvVars);
            if (result)
            {
                _logger.LogInformation(
                    "成功保存工具 {ToolId} 的用户环境变量配置，用户={Username}，提交 {SubmittedCount} 个，落库 {PersistedCount} 个",
                    toolId,
                    resolvedUsername,
                    normalizedEnvVars.Count,
                    persistedEnvVars.Count);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存工具 {ToolId} 的环境变量失败", toolId);
            return false;
        }
    }

    /// <summary>
    /// 删除指定工具的环境变量配置
    /// </summary>
    public async Task<bool> DeleteEnvironmentVariablesAsync(string toolId, string? username = null)
    {
        try
        {
            var resolvedUsername = ResolveUsername(username);
            if (string.IsNullOrWhiteSpace(resolvedUsername))
            {
                return false;
            }

            var result = await _userRepository.DeleteByToolIdAsync(resolvedUsername, toolId);
            if (result)
            {
                _logger.LogInformation("成功删除工具 {ToolId} 的用户环境变量配置，用户={Username}", toolId, resolvedUsername);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除工具 {ToolId} 的环境变量失败", toolId);
            return false;
        }
    }

    /// <summary>
    /// 重置为appsettings中的默认配置
    /// </summary>
    public async Task<Dictionary<string, string>> ResetToDefaultAsync(string toolId, string? username = null)
    {
        try
        {
            await DeleteEnvironmentVariablesAsync(toolId, username);
            return await GetEnvironmentVariablesAsync(toolId, username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重置工具 {ToolId} 的环境变量失败", toolId);
            return new Dictionary<string, string>();
        }
    }

    private string ResolveUsername(string? username)
    {
        return string.IsNullOrWhiteSpace(username)
            ? _userContextService.GetCurrentUsername()
            : username.Trim();
    }

    private async Task<Dictionary<string, string>> GetInheritedEnvironmentVariablesAsync(string toolId)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tool = _options.Tools.FirstOrDefault(t => t.Id == toolId);
        if (tool?.EnvironmentVariables != null)
        {
            ApplyOverrides(result, tool.EnvironmentVariables);
        }

        var dbEnvVars = await _repository.GetEnvironmentVariablesByToolIdAsync(toolId);
        ApplyOverrides(result, dbEnvVars);
        return result;
    }

    private static Dictionary<string, string> NormalizeEnvVars(Dictionary<string, string> envVars, bool keepEmptyValues)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in envVars)
        {
            var key = kvp.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = kvp.Value?.Trim() ?? string.Empty;
            if (!keepEmptyValues && string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static Dictionary<string, string> BuildPersistedUserEnvVars(
        Dictionary<string, string> requestedEnvVars,
        Dictionary<string, string> inheritedEnvVars)
    {
        var result = new Dictionary<string, string>(requestedEnvVars, StringComparer.OrdinalIgnoreCase);
        foreach (var inheritedKey in inheritedEnvVars.Keys)
        {
            if (!requestedEnvVars.ContainsKey(inheritedKey))
            {
                // 空字符串表示显式移除继承的环境变量，避免默认值在下次读取时再次合并回来。
                result[inheritedKey] = string.Empty;
            }
        }

        return result;
    }

    private static void ApplyOverrides(Dictionary<string, string> target, IEnumerable<KeyValuePair<string, string>> envVars)
    {
        foreach (var kvp in envVars)
        {
            var key = kvp.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = kvp.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                target.Remove(key);
                continue;
            }

            target[key] = value;
        }
    }
}
