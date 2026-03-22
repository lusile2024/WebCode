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
