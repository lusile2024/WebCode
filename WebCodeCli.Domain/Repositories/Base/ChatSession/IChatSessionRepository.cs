using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.ChatSession;

/// <summary>
/// 聊天会话仓储接口
/// </summary>
public interface IChatSessionRepository : IRepository<ChatSessionEntity>
{
    /// <summary>
    /// 根据用户名获取所有会话
    /// </summary>
    Task<List<ChatSessionEntity>> GetByUsernameAsync(string username);
    
    /// <summary>
    /// 根据会话ID和用户名获取会话
    /// </summary>
    Task<ChatSessionEntity?> GetByIdAndUsernameAsync(string sessionId, string username);
    
    /// <summary>
    /// 根据会话ID和用户名删除会话
    /// </summary>
    Task<bool> DeleteByIdAndUsernameAsync(string sessionId, string username);
    
    /// <summary>
    /// 根据用户名获取会话列表（按更新时间降序）
    /// </summary>
    Task<List<ChatSessionEntity>> GetByUsernameOrderByUpdatedAtAsync(string username);

    /// <summary>
    /// 根据飞书ChatKey获取所有会话
    /// </summary>
    /// <param name="feishuChatKey">飞书聊天键</param>
    /// <returns>会话列表</returns>
    Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey);

    /// <summary>
    /// 根据飞书ChatKey获取当前活跃会话
    /// </summary>
    /// <param name="feishuChatKey">飞书聊天键</param>
    /// <returns>活跃会话，如果不存在则返回null</returns>
    Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey);

    /// <summary>
    /// 设置指定会话为飞书活跃会话
    /// </summary>
    /// <param name="feishuChatKey">飞书聊天键</param>
    /// <param name="sessionId">要设置为活跃的会话ID</param>
    /// <returns>是否成功</returns>
    Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId);

    /// <summary>
    /// 关闭飞书会话
    /// </summary>
    /// <param name="feishuChatKey">飞书聊天键</param>
    /// <param name="sessionId">要关闭的会话ID</param>
    /// <returns>是否成功</returns>
    Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId);

    /// <summary>
    /// 创建飞书新会话
    /// </summary>
    /// <param name="feishuChatKey">飞书聊天键</param>
    /// <param name="username">用户名</param>
    /// <param name="workspacePath">工作区路径</param>
    /// <returns>新会话ID</returns>
    Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null);
}
