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
    /// 根据用户名 + 工具 + CLI ThreadId 查找会话（用于外部会话导入/去重）
    /// </summary>
    Task<ChatSessionEntity?> GetByUsernameToolAndCliThreadIdAsync(string username, string toolId, string cliThreadId);

    /// <summary>
    /// 根据工具 + CLI ThreadId 查找会话（跨用户，用于“一个外部会话只能被一个用户占用”约束）
    /// </summary>
    Task<ChatSessionEntity?> GetByToolAndCliThreadIdAsync(string toolId, string cliThreadId);

    /// <summary>
    /// 更新会话的 CLI ThreadId（用于恢复）
    /// </summary>
    Task<bool> UpdateCliThreadIdAsync(string sessionId, string cliThreadId);

    /// <summary>
    /// 更新会话的工作区绑定（仅更新数据库字段，不创建目录）
    /// </summary>
    Task<bool> UpdateWorkspaceBindingAsync(string sessionId, string? workspacePath, bool isCustomWorkspace);

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
    /// <param name="toolId">工具 ID</param>
    /// <returns>新会话ID</returns>
    Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null, string? toolId = null);
}
