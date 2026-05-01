namespace WebCodeCli.Domain.Domain.Model;

public sealed class CodexThreadProviderSyncResult
{
    public string SessionWorkspacePath { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string TargetProviderId { get; set; } = string.Empty;

    public string? SourceCodexHome { get; set; }

    public string? Message { get; set; }

    public bool SeededFromSource { get; set; }

    public List<string> UpdatedRolloutFiles { get; } = new();

    public List<string> SkippedRolloutFiles { get; } = new();

    public CodexThreadProviderSyncSqliteResult SqliteResult { get; set; } = new();

    public bool HasWarnings { get; set; }
}

public sealed class CodexThreadProviderSyncSqliteResult
{
    public string? DatabasePath { get; set; }

    public bool Exists { get; set; }

    public bool Missing { get; set; }

    public bool Locked { get; set; }

    public bool Updated { get; set; }

    public int RowsAffected { get; set; }

    public string? Message { get; set; }
}
