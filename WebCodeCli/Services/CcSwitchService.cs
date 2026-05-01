using System.Data;
using System.Data.SQLite;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly Regex TomlStringRegex = new(@"^\s*(?<key>[A-Za-z0-9_.-]+)\s*=\s*""(?<value>[^""]+)""", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly ILogger<CcSwitchService> _logger;
    private readonly Func<string> _configDirectoryResolver;
    private readonly IHttpClientFactory? _httpClientFactory;

    public CcSwitchService(ILogger<CcSwitchService> logger, IHttpClientFactory httpClientFactory)
        : this(logger, ResolveCcSwitchConfigDirectory, httpClientFactory)
    {
    }

    internal CcSwitchService(ILogger<CcSwitchService> logger, Func<string> configDirectoryResolver, IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;
        _configDirectoryResolver = configDirectoryResolver;
        _httpClientFactory = httpClientFactory;
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

    public async Task<CcSwitchModelCatalog> GetModelCatalogAsync(string toolId, string? providerId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = LoadContext();
        var toolStatus = BuildToolStatus(context, toolId);
        var catalog = new CcSwitchModelCatalog
        {
            ToolId = toolStatus.ToolId,
            ToolName = toolStatus.ToolName,
            AppType = toolStatus.AppType,
            IsManaged = toolStatus.IsManaged,
            IsDetected = toolStatus.IsDetected,
            ProviderId = providerId?.Trim(),
            ProviderName = toolStatus.ActiveProviderName,
            ProviderCategory = toolStatus.ActiveProviderCategory,
            StatusMessage = toolStatus.StatusMessage
        };

        if (!toolStatus.IsManaged)
        {
            return catalog;
        }

        if (!TryMapTool(toolId, out var appType, out _, out _))
        {
            return catalog;
        }

        var resolvedProviderId = string.IsNullOrWhiteSpace(providerId)
            ? toolStatus.ActiveProviderId
            : providerId.Trim();
        if (string.IsNullOrWhiteSpace(resolvedProviderId))
        {
            return catalog;
        }

        var providerRecord = context.GetProvider(appType, resolvedProviderId);
        if (providerRecord == null)
        {
            catalog.ProviderId = resolvedProviderId;
            catalog.StatusMessage = $"未找到 Provider `{resolvedProviderId}` 的配置记录。";
            return catalog;
        }

        catalog.ProviderId = providerRecord.Id;
        catalog.ProviderName = providerRecord.Name;
        catalog.ProviderCategory = providerRecord.Category;

        var fallbackOptions = BuildFallbackModelOptions(providerRecord);
        try
        {
            var remoteOptions = await TryFetchRemoteModelsAsync(providerRecord, cancellationToken);
            if (remoteOptions.Count > 0)
            {
                catalog.Models = MergeModelOptions(remoteOptions, fallbackOptions);
                catalog.IsRemoteFetched = true;
                catalog.StatusMessage = $"已从 Provider `{providerRecord.Name}` 拉取可用模型。";
                return catalog;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取 cc-switch Provider 模型列表失败: ToolId={ToolId}, ProviderId={ProviderId}", toolId, providerRecord.Id);
        }

        catalog.Models = fallbackOptions;
        if (catalog.Models.Count > 0)
        {
            catalog.StatusMessage = $"未能实时拉取 Provider `{providerRecord.Name}` 模型列表，已回退到当前默认模型。";
        }
        else
        {
            catalog.StatusMessage = $"未能从 Provider `{providerRecord.Name}` 获取模型列表。";
        }

        return catalog;
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
            command.CommandText = "SELECT id, app_type, name, category, settings_config, provider_type, meta FROM providers";
            using var reader = command.ExecuteReader(CommandBehavior.CloseConnection);
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var appType = reader.GetString(1);
                var name = reader.GetString(2);
                var category = reader.IsDBNull(3) ? null : reader.GetString(3);
                var settingsConfig = reader.IsDBNull(4) ? null : reader.GetString(4);
                var providerType = reader.IsDBNull(5) ? null : reader.GetString(5);
                var meta = reader.IsDBNull(6) ? null : reader.GetString(6);

                context.ProviderIndex[(appType, id)] = new ProviderRecord
                {
                    Id = id,
                    AppType = appType,
                    Name = name,
                    Category = category,
                    SettingsConfig = settingsConfig,
                    ProviderType = providerType,
                    Meta = meta
                };
            }

            using var endpointCommand = connection.CreateCommand();
            endpointCommand.CommandText = "SELECT provider_id, app_type, url FROM provider_endpoints";
            using var endpointReader = endpointCommand.ExecuteReader();
            while (endpointReader.Read())
            {
                var providerId = endpointReader.GetString(0);
                var appType = endpointReader.GetString(1);
                var url = endpointReader.IsDBNull(2) ? null : endpointReader.GetString(2);
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (context.ProviderIndex.TryGetValue((appType, providerId), out var record))
                {
                    record.EndpointUrl = url.Trim();
                }
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
        public string? SettingsConfig { get; set; }
        public string? ProviderType { get; set; }
        public string? Meta { get; set; }
        public string? EndpointUrl { get; set; }
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

    private async Task<List<CcSwitchModelOption>> TryFetchRemoteModelsAsync(ProviderRecord providerRecord, CancellationToken cancellationToken)
    {
        if (_httpClientFactory == null)
        {
            return [];
        }

        return providerRecord.AppType switch
        {
            "claude" => await TryFetchAnthropicModelsAsync(providerRecord, cancellationToken),
            "codex" => await TryFetchOpenAiCompatibleModelsAsync(providerRecord, cancellationToken),
            "opencode" => await TryFetchOpenAiCompatibleModelsAsync(providerRecord, cancellationToken),
            _ => []
        };
    }

    private async Task<List<CcSwitchModelOption>> TryFetchAnthropicModelsAsync(ProviderRecord providerRecord, CancellationToken cancellationToken)
    {
        var settings = ParseSettingsConfig(providerRecord.SettingsConfig);
        var env = settings.TryGetProperty("env", out var envNode) && envNode.ValueKind == JsonValueKind.Object
            ? envNode
            : default;
        var apiKey = TryGetJsonString(env, "ANTHROPIC_AUTH_TOKEN");
        var baseUrl = FirstNonEmpty(
            TryGetJsonString(env, "ANTHROPIC_BASE_URL"),
            providerRecord.EndpointUrl);

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(baseUrl))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildAnthropicModelsUrl(baseUrl));
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        return await SendModelListRequestAsync(request, cancellationToken);
    }

    private async Task<List<CcSwitchModelOption>> TryFetchOpenAiCompatibleModelsAsync(ProviderRecord providerRecord, CancellationToken cancellationToken)
    {
        var settings = ParseSettingsConfig(providerRecord.SettingsConfig);
        var auth = settings.TryGetProperty("auth", out var authNode) && authNode.ValueKind == JsonValueKind.Object
            ? authNode
            : default;
        var configText = TryGetJsonString(settings, "config");
        var apiKey = FirstNonEmpty(
            TryGetJsonString(auth, "OPENAI_API_KEY"),
            TryGetJsonString(auth, "apiKey"));
        var baseUrl = FirstNonEmpty(
            TryGetTomlString(configText, "base_url"),
            providerRecord.EndpointUrl);

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(baseUrl))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildOpenAiModelsUrl(baseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return await SendModelListRequestAsync(request, cancellationToken);
    }

    private async Task<List<CcSwitchModelOption>> SendModelListRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory!.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var options = new List<CcSwitchModelOption>();
        foreach (var item in dataElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = FirstNonEmpty(
                TryGetJsonString(item, "id"),
                TryGetJsonString(item, "model"));
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var displayName = FirstNonEmpty(
                TryGetJsonString(item, "display_name"),
                TryGetJsonString(item, "name"),
                id);

            options.Add(new CcSwitchModelOption
            {
                Id = id.Trim(),
                DisplayName = displayName!.Trim()
            });
        }

        return MergeModelOptions(options);
    }

    private static List<CcSwitchModelOption> BuildFallbackModelOptions(ProviderRecord providerRecord)
    {
        var settings = ParseSettingsConfig(providerRecord.SettingsConfig);
        var fallbackModels = providerRecord.AppType switch
        {
            "claude" => BuildClaudeFallbackModels(settings),
            "codex" => BuildTomlFallbackModels(settings),
            "opencode" => BuildTomlFallbackModels(settings),
            _ => []
        };

        return MergeModelOptions(fallbackModels);
    }

    private static List<CcSwitchModelOption> BuildClaudeFallbackModels(JsonElement settings)
    {
        var result = new List<CcSwitchModelOption>();
        if (!settings.TryGetProperty("env", out var envNode) || envNode.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        AddOptionIfPresent(result, TryGetJsonString(envNode, "ANTHROPIC_MODEL"));
        AddOptionIfPresent(result, TryGetJsonString(envNode, "ANTHROPIC_DEFAULT_OPUS_MODEL"));
        AddOptionIfPresent(result, TryGetJsonString(envNode, "ANTHROPIC_DEFAULT_SONNET_MODEL"));
        AddOptionIfPresent(result, TryGetJsonString(envNode, "ANTHROPIC_DEFAULT_HAIKU_MODEL"));
        return result;
    }

    private static List<CcSwitchModelOption> BuildTomlFallbackModels(JsonElement settings)
    {
        var result = new List<CcSwitchModelOption>();
        var configText = TryGetJsonString(settings, "config");
        AddOptionIfPresent(result, TryGetTomlString(configText, "model"));
        return result;
    }

    private static void AddOptionIfPresent(List<CcSwitchModelOption> options, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        options.Add(new CcSwitchModelOption
        {
            Id = value.Trim(),
            DisplayName = value.Trim()
        });
    }

    private static List<CcSwitchModelOption> MergeModelOptions(params IEnumerable<CcSwitchModelOption>[] collections)
    {
        var result = new List<CcSwitchModelOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var collection in collections)
        {
            foreach (var option in collection)
            {
                if (string.IsNullOrWhiteSpace(option?.Id))
                {
                    continue;
                }

                var id = option.Id.Trim();
                if (!seen.Add(id))
                {
                    continue;
                }

                result.Add(new CcSwitchModelOption
                {
                    Id = id,
                    DisplayName = string.IsNullOrWhiteSpace(option.DisplayName) ? id : option.DisplayName.Trim()
                });
            }
        }

        return result;
    }

    private static JsonElement ParseSettingsConfig(string? settingsConfig)
    {
        if (string.IsNullOrWhiteSpace(settingsConfig))
        {
            using var doc = JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }

        try
        {
            using var doc = JsonDocument.Parse(settingsConfig);
            return doc.RootElement.Clone();
        }
        catch
        {
            using var doc = JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString()?.Trim() : property.ToString()?.Trim();
    }

    private static string? TryGetTomlString(string? text, string key)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in TomlStringRegex.Matches(text))
        {
            if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["value"].Value.Trim();
            }
        }

        return null;
    }

    private static string BuildAnthropicModelsUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return $"{trimmed}/v1/models";
    }

    private static string BuildOpenAiModelsUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{trimmed}/models"
            : $"{trimmed}/v1/models";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
