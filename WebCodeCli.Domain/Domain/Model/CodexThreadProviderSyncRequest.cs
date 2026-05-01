namespace WebCodeCli.Domain.Domain.Model;

public sealed class CodexThreadProviderSyncRequest
{
    public string SessionWorkspacePath { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string TargetProviderId { get; set; } = string.Empty;
}
