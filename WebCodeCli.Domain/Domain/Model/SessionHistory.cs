namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// 会话历史记录
/// </summary>
public class SessionHistory
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;
    
    /// <summary>
    /// 会话标题
    /// </summary>
    public string Title { get; set; } = "新会话";
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 工作区路径
    /// </summary>
    public string WorkspacePath { get; set; } = string.Empty;
    
    /// <summary>
    /// 选中的工具ID
    /// </summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// CLI 会话/线程 ID（用于恢复外部 CLI 会话，例如 codex thread、claude resume、opencode session）
    /// </summary>
    public string? CliThreadId { get; set; }

    /// <summary>
    /// 是否使用由 cc-switch 派生的会话级 Provider 快照
    /// </summary>
    public bool UsesCcSwitchSnapshot { get; set; }

    /// <summary>
    /// 会话快照对应的工具 ID
    /// </summary>
    public string? CcSwitchSnapshotToolId { get; set; }

    /// <summary>
    /// 会话固定的 Provider ID
    /// </summary>
    public string? CcSwitchProviderId { get; set; }

    /// <summary>
    /// 会话固定的 Provider 名称
    /// </summary>
    public string? CcSwitchProviderName { get; set; }

    /// <summary>
    /// 会话固定的 Provider 分类
    /// </summary>
    public string? CcSwitchProviderCategory { get; set; }

    /// <summary>
    /// 会话快照来源的 live 配置路径
    /// </summary>
    public string? CcSwitchLiveConfigPath { get; set; }

    /// <summary>
    /// 会话工作区内的配置快照相对路径
    /// </summary>
    public string? CcSwitchSnapshotRelativePath { get; set; }

    /// <summary>
    /// 最近一次同步会话快照的时间
    /// </summary>
    public DateTime? CcSwitchSnapshotSyncedAt { get; set; }
    
    /// <summary>
    /// 消息列表
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = new();
    
    /// <summary>
    /// 工作区是否有效
    /// </summary>
    public bool IsWorkspaceValid { get; set; } = true;

    /// <summary>
    /// 是否为自定义工作目录（非系统自动生成的临时目录）
    /// </summary>
    public bool IsCustomWorkspace { get; set; } = false;
    
    /// <summary>
    /// 关联的项目ID（可选）
    /// </summary>
    public string? ProjectId { get; set; }
    
    /// <summary>
    /// 关联的项目名称（仅用于显示）
    /// </summary>
    public string? ProjectName { get; set; }
}
