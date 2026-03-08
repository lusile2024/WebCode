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

    /// <summary>
    /// 获取聊天的当前活跃会话ID
    /// </summary>
    /// <param name="chatKey">聊天键（格式：feishu:{AppId}:{ChatId}）</param>
    /// <returns>当前会话ID，如果不存在则返回null</returns>
    string? GetCurrentSession(string chatKey);

    /// <summary>
    /// 获取会话的最后活跃时间
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>最后活跃时间，如果会话不存在则返回null</returns>
    DateTime? GetSessionLastActiveTime(string sessionId);

    /// <summary>
    /// 获取聊天的所有会话ID列表
    /// </summary>
    /// <param name="chatKey">聊天键</param>
    /// <returns>会话ID列表</returns>
    List<string> GetChatSessions(string chatKey);

    /// <summary>
    /// 切换聊天的当前活跃会话
    /// </summary>
    /// <param name="chatKey">聊天键</param>
    /// <param name="sessionId">要切换到的会话ID</param>
    /// <returns>是否切换成功</returns>
    bool SwitchCurrentSession(string chatKey, string sessionId);

    /// <summary>
    /// 关闭指定会话
    /// </summary>
    /// <param name="chatKey">聊天键</param>
    /// <param name="sessionId">要关闭的会话ID</param>
    /// <returns>是否关闭成功</returns>
    bool CloseSession(string chatKey, string sessionId);

    /// <summary>
    /// 创建新会话
    /// </summary>
    /// <param name="message">飞书 incoming 消息</param>
    /// <param name="customWorkspacePath">自定义工作区路径（可选）</param>
    /// <returns>新会话ID</returns>
    string CreateNewSession(FeishuIncomingMessage message, string? customWorkspacePath = null);
}
