using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书渠道服务接口
/// </summary>
public interface IFeishuChannelService
{
    /// <summary>
    /// 服务是否运行中
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 发送文本消息
    /// </summary>
    /// <param name="chatId">会话 ID</param>
    /// <param name="content">消息内容</param>
    /// <returns>消息 ID</returns>
    Task<string> SendMessageAsync(string chatId, string content);

    /// <summary>
    /// 回复消息
    /// </summary>
    /// <param name="messageId">要回复的消息 ID</param>
    /// <param name="content">回复内容</param>
    /// <returns>回复消息 ID</returns>
    Task<string> ReplyMessageAsync(string messageId, string content);

    /// <summary>
    /// 发送流式消息（核心方法）
    /// </summary>
    /// <param name="chatId">会话 ID</param>
    /// <param name="initialContent">初始内容</param>
    /// <param name="replyToMessageId">要回复的消息 ID（可选）</param>
    /// <returns>流式句柄</returns>
    Task<FeishuStreamingHandle> SendStreamingMessageAsync(
        string chatId,
        string initialContent,
        string? replyToMessageId = null);

    /// <summary>
    /// 处理收到的消息（由 FeishuMessageHandler 调用）
    /// </summary>
    /// <param name="message">收到的消息</param>
    Task HandleIncomingMessageAsync(FeishuIncomingMessage message);
}
