using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.ChatSession;

/// <summary>
/// 聊天会话实体
/// </summary>
[SugarTable("ChatSession")]
[SugarIndex("idx_feishu_chatkey", nameof(FeishuChatKey), OrderByType.Asc)]
public class ChatSessionEntity
{
    /// <summary>
    /// 会话ID（主键）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string SessionId { get; set; } = string.Empty;
    
    /// <summary>
    /// 用户名（多用户支持）
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string Username { get; set; } = "default";
    
    /// <summary>
    /// 会话标题
    /// </summary>
    [SugarColumn(Length = 256, IsNullable = true)]
    public string? Title { get; set; }
    
    /// <summary>
    /// 工作区路径
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? WorkspacePath { get; set; }
    
    /// <summary>
    /// 使用的工具ID
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ToolId { get; set; }

    /// <summary>
    /// CLI 会话/线程 ID（用于恢复外部 CLI 会话，例如 codex thread、claude resume、opencode session）
    /// </summary>
    [SugarColumn(Length = 256, IsNullable = true)]
    public string? CliThreadId { get; set; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 更新时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 工作区是否有效
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public bool IsWorkspaceValid { get; set; } = true;
    
    /// <summary>
    /// 关联的项目ID（可选）
    /// </summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ProjectId { get; set; }

    /// <summary>
    /// 飞书聊天唯一标识（小写的ChatId，作为渠道关联键）
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? FeishuChatKey { get; set; }

    /// <summary>
    /// 是否为当前聊天的活跃会话（每个ChatKey只能有一个活跃会话）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public bool IsFeishuActive { get; set; } = false;

    /// <summary>
    /// 是否为自定义工作目录（非系统自动生成的临时目录）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public bool IsCustomWorkspace { get; set; } = false;
}
