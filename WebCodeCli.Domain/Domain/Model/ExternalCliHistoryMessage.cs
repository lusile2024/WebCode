namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// 外部 CLI 会话历史消息
/// </summary>
public class ExternalCliHistoryMessage
{
    /// <summary>
    /// 消息角色，通常为 user / assistant
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 解析后的纯文本内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间（如果可解析）
    /// </summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// 原始消息类型（可选）
    /// </summary>
    public string? RawType { get; set; }
}
