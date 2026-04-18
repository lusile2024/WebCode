namespace WebCodeCli.Domain.Domain.Service;

public interface ICcSwitchService
{
    bool IsManagedTool(string toolId);
    Task<CcSwitchStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<CcSwitchToolStatus> GetToolStatusAsync(string toolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, CcSwitchToolStatus>> GetToolStatusesAsync(IEnumerable<string> toolIds, CancellationToken cancellationToken = default);
}

public class CcSwitchStatus
{
    public bool IsDetected { get; set; }
    public string? ConfigDirectory { get; set; }
    public string? DatabasePath { get; set; }
    public string? SettingsPath { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, CcSwitchToolStatus> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class CcSwitchToolStatus
{
    public string ToolId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string AppType { get; set; } = string.Empty;
    public string ConfigFileName { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
    public bool IsDetected { get; set; }
    public bool HasDatabase { get; set; }
    public bool HasSettingsFile { get; set; }
    public bool HasActiveProvider => !string.IsNullOrWhiteSpace(ActiveProviderId);
    public bool HasLiveConfig { get; set; }
    public bool UsesSettingsOverride { get; set; }
    public bool IsLaunchReady { get; set; }
    public string? ConfigDirectory { get; set; }
    public string? DatabasePath { get; set; }
    public string? SettingsPath { get; set; }
    public string? LiveConfigPath { get; set; }
    public string? ActiveProviderId { get; set; }
    public string? ActiveProviderName { get; set; }
    public string? ActiveProviderCategory { get; set; }
    public string? StatusMessage { get; set; }
}

public class CcSwitchSessionSnapshot
{
    public bool UsesSnapshot { get; set; }
    public string? ToolId { get; set; }
    public string? ProviderId { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderCategory { get; set; }
    public string? SourceLiveConfigPath { get; set; }
    public string? SnapshotRelativePath { get; set; }
    public DateTime? SyncedAt { get; set; }
}
