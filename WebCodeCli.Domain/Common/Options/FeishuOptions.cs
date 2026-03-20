namespace WebCodeCli.Domain.Common.Options;

/// <summary>
/// 飞书渠道配置选项
/// </summary>
public class FeishuOptions
{
    /// <summary>
    /// 是否启用飞书渠道
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 应用 ID
    /// </summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// 应用密钥
    /// </summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// 加密密钥（用于解密事件推送）
    /// </summary>
    public string EncryptKey { get; set; } = string.Empty;

    /// <summary>
    /// 验证令牌
    /// </summary>
    public string VerificationToken { get; set; } = string.Empty;

    /// <summary>
    /// 流式更新节流间隔（毫秒）
    /// </summary>
    public int StreamingThrottleMs { get; set; } = 500;

    /// <summary>
    /// HTTP 请求超时时间（秒）
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 默认卡片标题
    /// </summary>
    public string DefaultCardTitle { get; set; } = "AI 助手";

    /// <summary>
    /// 思考中提示消息
    /// </summary>
    public string ThinkingMessage { get; set; } = "⏳ 思考中...";

    /// <summary>
    /// 飞书渠道默认使用的 CLI 工具 ID
    /// 未配置时会按 claude-code -> codex -> opencode -> 首个可用工具 的顺序回退
    /// </summary>
    public string DefaultToolId { get; set; } = "claude-code";
}
