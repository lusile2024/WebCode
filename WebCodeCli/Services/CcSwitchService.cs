using System.Data;
using System.Data.SQLite;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Services;

[ServiceDescription(typeof(ICcSwitchService), ServiceLifetime.Singleton)]
public class CcSwitchService : ICcSwitchService
{
    private const string ClaudeToolId = "claude-code";
    private const string CodexToolId = "codex";
    private const string OpenCodeToolId = "opencode";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<CcSwitchService> _logger;
    private readonly Func<string> _configDirectoryResolver;

    public CcSwitchService(ILogger<CcSwitchService> logger)
        : this(logger, ResolveCcSwitchConfigDirectory)
    {
    }

    internal CcSwitchService(ILogger<CcSwitchService> logger, Func<string> configDirectoryResolver)
    {
        _logger = logger;
        _configDirectoryResolver = configDirectoryResolver;
    }

    public bool IsManagedTool(string toolId)
    {
        return TryMapTool(toolId, out _, out _, out _);
    }

    public Task<CcSwitchStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = LoadContext();
        var status = new CcSwitchStatus
        {
            IsDetected = context.IsDetected,
            ConfigDirectory = context.ConfigDirectory,
            DatabasePath = context.DatabasePath,
            SettingsPath = context.SettingsPath,
            ErrorMessage = context.GlobalError
        };

        foreach (var toolId in new[] { ClaudeToolId, CodexToolId, OpenCodeToolId })
        {
            status.Tools[toolId] = BuildToolStatus(context, toolId);
        }

        return Task.FromResult(status);
    }

    public Task<CcSwitchToolStatus> GetToolStatusAsync(string toolId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = LoadContext();
        return Task.FromResult(BuildToolStatus(context, toolId));
    }

    public Task<IReadOnlyDictionary<string, CcSwitchToolStatus>> GetToolStatusesAsync(IEnumerable<string> toolIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = LoadContext();
        var result = new Dictionary<string, CcSwitchToolStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolId in toolIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            result[toolId] = BuildToolStatus(context, toolId);
        }

        return Task.FromResult<IReadOnlyDictionary<string, CcSwitchToolStatus>>(result);
    }

    private CcSwitchToolStatus BuildToolStatus(CcSwitchContext context, string toolId)
    {
        if (!TryMapTool(toolId, out var appType, out var toolName, out var configFileName))
        {
            return new CcSwitchToolStatus
            {
                ToolId = toolId,
                ToolName = toolId,
                IsManaged = false,
                StatusMessage = "该工具不受 cc-switch 管理。"
            };
        }

        var status = new CcSwitchToolStatus
        {
            ToolId = toolId,
            ToolName = toolName,
            AppType = appType,
            ConfigFileName = configFileName,
            IsManaged = true,
            IsDetected = context.IsDetected,
            HasDatabase = context.HasDatabase,
            HasSettingsFile = context.HasSettingsFile,
            ConfigDirectory = context.GetToolConfigDirectory(appType),
            DatabasePath = context.DatabasePath,
            SettingsPath = context.SettingsPath
        };

        if (!status.IsDetected)
        {
            status.StatusMessage = $"未检测到 cc-switch 配置目录，{toolName} 无法使用。";
            return status;
        }

        if (!string.IsNullOrWhiteSpace(context.GlobalError))
        {
            status.StatusMessage = $"{toolName} 无法使用：{context.GlobalError}";
            return status;
        }

        if (!status.HasDatabase)
        {
            status.StatusMessage = $"未找到 cc-switch.db，无法读取 {toolName} 当前激活的 Provider。";
            return status;
        }

        status.UsesSettingsOverride = context.TryGetCurrentProviderOverride(appType, out var currentProviderId);
        if (string.IsNullOrWhiteSpace(currentProviderId))
        {
            currentProviderId = context.GetCurrentProviderFromDatabase(appType);
        }

        if (string.IsNullOrWhiteSpace(currentProviderId))
        {
            status.StatusMessage = $"{toolName} 当前未在 cc-switch 中激活 Provider。";
            return status;
        }

        status.ActiveProviderId = currentProviderId;

        var providerRecord = context.GetProvider(appType, currentProviderId);
        if (providerRecord == null)
        {
            status.StatusMessage = $"{toolName} 在 cc-switch 中激活的 Provider 记录不存在或已损坏。";
            return status;
        }

        status.ActiveProviderName = providerRecord.Name;
        status.ActiveProviderCategory = providerRecord.Category;
        status.LiveConfigPath = Path.Combine(status.ConfigDirectory ?? string.Empty, configFileName);
        status.HasLiveConfig = !string.IsNullOrWhiteSpace(status.LiveConfigPath) && File.Exists(status.LiveConfigPath);

        if (!status.HasLiveConfig)
        {
            status.StatusMessage = $"未找到 {toolName} 的 live 配置文件，cc-switch 可能尚未完成同步。";
            return status;
        }

        status.IsLaunchReady = true;
        status.StatusMessage = $"{toolName} 已由 cc-switch 管理并可直接启动。";
        return status;
    }

    private CcSwitchContext LoadContext()
    {
        var configDirectory = _configDirectoryResolver();
        var databasePath = Path.Combine(configDirectory, "cc-switch.db");
        var settingsPath = Path.Combine(configDirectory, "settings.json");
        var hasConfigDirectory = Directory.Exists(configDirectory);
        var hasDatabase = File.Exists(databasePath);
        var hasSettingsFile = File.Exists(settingsPath);
        var context = new CcSwitchContext(configDirectory, databasePath, settingsPath, hasConfigDirectory || hasDatabase || hasSettingsFile, hasDatabase, hasSettingsFile);

        if (!context.IsDetected)
        {
            return context;
        }

        if (hasSettingsFile)
        {
            try
            {
                var settingsJson = File.ReadAllText(settingsPath);
                context.Settings = JsonSerializer.Deserialize<CcSwitchSettingsSnapshot>(settingsJson, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取 cc-switch settings.json 失败: {Path}", settingsPath);
                context.GlobalError = "cc-switch settings.json 无法读取或格式无效。";
                return context;
            }
        }

        if (!hasDatabase)
        {
            return context;
        }

        try
        {
            using var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;Read Only=True;FailIfMissing=True;Pooling=False;");
            connection.Open();

            foreach (var appType in new[] { "claude", "codex", "opencode" })
            {
                context.DatabaseCurrentProviders[appType] = QueryCurrentProviderId(connection, appType);
            }

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, app_type, name, category FROM providers";
            using var reader = command.ExecuteReader(CommandBehavior.CloseConnection);
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var appType = reader.GetString(1);
                var name = reader.GetString(2);
                var category = reader.IsDBNull(3) ? null : reader.GetString(3);

                context.ProviderIndex[(appType, id)] = new ProviderRecord
                {
                    Id = id,
                    AppType = appType,
                    Name = name,
                    Category = category
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 cc-switch.db 失败: {Path}", databasePath);
            context.GlobalError = "cc-switch.db 无法读取或格式无效。";
        }

        return context;
    }

    private static string? QueryCurrentProviderId(SQLiteConnection connection, string appType)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM providers WHERE app_type = @appType AND is_current = 1 LIMIT 1";
        command.Parameters.AddWithValue("@appType", appType);
        var result = command.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : Convert.ToString(result);
    }

    private static bool TryMapTool(string toolId, out string appType, out string toolName, out string configFileName)
    {
        switch (toolId.Trim().ToLowerInvariant())
        {
            case ClaudeToolId:
                appType = "claude";
                toolName = "Claude Code";
                configFileName = "settings.json";
                return true;
            case CodexToolId:
                appType = "codex";
                toolName = "Codex";
                configFileName = "config.toml";
                return true;
            case OpenCodeToolId:
                appType = "opencode";
                toolName = "OpenCode";
                configFileName = "opencode.json";
                return true;
            default:
                appType = string.Empty;
                toolName = string.Empty;
                configFileName = string.Empty;
                return false;
        }
    }

    private static string ResolveCcSwitchConfigDirectory()
    {
        var homeDirectory = ResolveUserHomeDirectory();
        var defaultDirectory = Path.Combine(homeDirectory, ".cc-switch");

        if (OperatingSystem.IsWindows())
        {
            var defaultDatabasePath = Path.Combine(defaultDirectory, "cc-switch.db");
            if (!File.Exists(defaultDatabasePath))
            {
                var legacyHome = Environment.GetEnvironmentVariable("HOME")?.Trim();
                if (!string.IsNullOrWhiteSpace(legacyHome))
                {
                    var legacyDirectory = Path.Combine(legacyHome, ".cc-switch");
                    var legacyDatabasePath = Path.Combine(legacyDirectory, "cc-switch.db");
                    if (File.Exists(legacyDatabasePath))
                    {
                        return legacyDirectory;
                    }
                }
            }
        }

        return defaultDirectory;
    }

    private static string ResolveUserHomeDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return userProfile;
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        return Directory.GetCurrentDirectory();
    }

    private sealed class CcSwitchContext
    {
        public CcSwitchContext(string configDirectory, string databasePath, string settingsPath, bool isDetected, bool hasDatabase, bool hasSettingsFile)
        {
            ConfigDirectory = configDirectory;
            DatabasePath = databasePath;
            SettingsPath = settingsPath;
            IsDetected = isDetected;
            HasDatabase = hasDatabase;
            HasSettingsFile = hasSettingsFile;
        }

        public string ConfigDirectory { get; }
        public string DatabasePath { get; }
        public string SettingsPath { get; }
        public bool IsDetected { get; }
        public bool HasDatabase { get; }
        public bool HasSettingsFile { get; }
        public string? GlobalError { get; set; }
        public CcSwitchSettingsSnapshot? Settings { get; set; }
        public Dictionary<string, string?> DatabaseCurrentProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<(string AppType, string ProviderId), ProviderRecord> ProviderIndex { get; } = new();

        public bool TryGetCurrentProviderOverride(string appType, out string? providerId)
        {
            providerId = appType switch
            {
                "claude" => Settings?.CurrentProviderClaude,
                "codex" => Settings?.CurrentProviderCodex,
                "opencode" => Settings?.CurrentProviderOpencode,
                _ => null
            };

            providerId = providerId?.Trim();
            return !string.IsNullOrWhiteSpace(providerId);
        }

        public string? GetCurrentProviderFromDatabase(string appType)
        {
            return DatabaseCurrentProviders.TryGetValue(appType, out var providerId)
                ? providerId?.Trim()
                : null;
        }

        public ProviderRecord? GetProvider(string appType, string providerId)
        {
            return ProviderIndex.TryGetValue((appType, providerId), out var record) ? record : null;
        }

        public string GetToolConfigDirectory(string appType)
        {
            return appType switch
            {
                "claude" => NormalizePath(Settings?.ClaudeConfigDir) ?? Path.Combine(ResolveUserHomeDirectory(), ".claude"),
                "codex" => NormalizePath(Settings?.CodexConfigDir) ?? Path.Combine(ResolveUserHomeDirectory(), ".codex"),
                "opencode" => NormalizePath(Settings?.OpencodeConfigDir) ?? Path.Combine(ResolveUserHomeDirectory(), ".config", "opencode"),
                _ => ResolveUserHomeDirectory()
            };
        }

        private static string? NormalizePath(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    private sealed class ProviderRecord
    {
        public string Id { get; set; } = string.Empty;
        public string AppType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
    }

    private sealed class CcSwitchSettingsSnapshot
    {
        [JsonPropertyName("claudeConfigDir")]
        public string? ClaudeConfigDir { get; set; }

        [JsonPropertyName("codexConfigDir")]
        public string? CodexConfigDir { get; set; }

        [JsonPropertyName("opencodeConfigDir")]
        public string? OpencodeConfigDir { get; set; }

        [JsonPropertyName("currentProviderClaude")]
        public string? CurrentProviderClaude { get; set; }

        [JsonPropertyName("currentProviderCodex")]
        public string? CurrentProviderCodex { get; set; }

        [JsonPropertyName("currentProviderOpencode")]
        public string? CurrentProviderOpencode { get; set; }
    }
}
