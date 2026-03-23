namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// 外部 CLI 会话摘要（用于发现/导入/恢复）
/// </summary>
public sealed class ExternalCliSessionSummary
{
    /// <summary>
    /// 工具 ID（codex / claude-code / opencode）
    /// </summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// 工具名称（用于展示）
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// CLI 侧的会话/线程 ID（用于 resume）
    /// </summary>
    public string CliThreadId { get; set; } = string.Empty;

    /// <summary>
    /// 会话标题（如果可解析到）
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 会话工作目录（如果可解析到）
    /// </summary>
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// 最后更新时间（如果可解析到）
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 是否已被当前 Web 用户导入为 WebCode 会话
    /// </summary>
    public bool AlreadyImported { get; set; }

    /// <summary>
    /// 已导入的 WebCode 会话 ID（若已导入）
    /// </summary>
    public string? ImportedSessionId { get; set; }
}

public sealed class ImportExternalCliSessionRequest
{
    public string ToolId { get; set; } = string.Empty;
    public string CliThreadId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? WorkspacePath { get; set; }
}

public sealed class ImportExternalCliSessionResult
{
    public bool Success { get; set; }
    public bool AlreadyExists { get; set; }
    public string? SessionId { get; set; }
    public string? ErrorMessage { get; set; }
}

