using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.ChatSession;

/// <summary>
/// 聊天会话仓储实现
/// </summary>
[ServiceDescription(typeof(IChatSessionRepository), ServiceLifetime.Scoped)]
public class ChatSessionRepository : Repository<ChatSessionEntity>, IChatSessionRepository
{
    public ChatSessionRepository(ISqlSugarClient context = null) : base(context)
    {
    }

    /// <summary>
    /// 根据用户名获取所有会话
    /// </summary>
    public async Task<List<ChatSessionEntity>> GetByUsernameAsync(string username)
    {
        return await GetListAsync(x => x.Username == username);
    }

    /// <summary>
    /// 根据会话ID和用户名获取会话
    /// </summary>
    public async Task<ChatSessionEntity?> GetByIdAndUsernameAsync(string sessionId, string username)
    {
        return await GetFirstAsync(x => x.SessionId == sessionId && x.Username == username);
    }

    /// <summary>
    /// 根据会话ID和用户名删除会话
    /// </summary>
    public async Task<bool> DeleteByIdAndUsernameAsync(string sessionId, string username)
    {
        return await DeleteAsync(x => x.SessionId == sessionId && x.Username == username);
    }

    /// <summary>
    /// 根据用户名获取会话列表（按更新时间降序）
    /// </summary>
    public async Task<List<ChatSessionEntity>> GetByUsernameOrderByUpdatedAtAsync(string username)
    {
        return await GetDB().Queryable<ChatSessionEntity>()
            .Where(x => x.Username == username)
            .OrderBy(x => x.UpdatedAt, OrderByType.Desc)
            .ToListAsync();
    }

    /// <summary>
    /// 根据飞书ChatKey获取所有会话
    /// </summary>
    public async Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey)
    {
        return await GetDB().Queryable<ChatSessionEntity>()
            .Where(s => s.FeishuChatKey == feishuChatKey)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// 根据飞书ChatKey获取当前活跃会话
    /// </summary>
    public async Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey)
    {
        return await GetFirstAsync(s => s.FeishuChatKey == feishuChatKey && s.IsFeishuActive);
    }

    /// <summary>
    /// 设置指定会话为飞书活跃会话
    /// </summary>
    public async Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId)
    {
        try
        {
            // 使用事务
            await GetDB().Ado.BeginTranAsync();

            // 将该聊天下所有会话设为非活跃
            await GetDB().Updateable<ChatSessionEntity>()
                .SetColumns(s => s.IsFeishuActive == false)
                .Where(s => s.FeishuChatKey == feishuChatKey)
                .ExecuteCommandAsync();
            // 设置目标会话为活跃
            var rowsAffected = await GetDB().Updateable<ChatSessionEntity>()
                .SetColumns(s => s.IsFeishuActive == true)
                .SetColumns(s => s.UpdatedAt == DateTime.Now)
                .Where(s => s.SessionId == sessionId && s.FeishuChatKey == feishuChatKey)
                .ExecuteCommandAsync();

            await GetDB().Ado.CommitTranAsync();
            return rowsAffected > 0;
        }
        catch
        {
            await GetDB().Ado.RollbackTranAsync();
            throw;
        }
    }

    /// <summary>
    /// 关闭飞书会话
    /// </summary>
    public async Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId)
    {
        try
        {
            // 使用事务
            await GetDB().Ado.BeginTranAsync();

            // 检查是否为活跃会话
            var session = await GetFirstAsync(s => s.SessionId == sessionId && s.FeishuChatKey == feishuChatKey);
            if (session == null)
            {
                await GetDB().Ado.RollbackTranAsync();
                return false;
            }
            var wasActive = session.IsFeishuActive;
            // 删除会话
            await GetDB().Deleteable<ChatSessionEntity>()
                .Where(s => s.SessionId == sessionId)
                .ExecuteCommandAsync();
            // 如果删除的是活跃会话，找最新的会话设为活跃
            if (wasActive)
            {
                var latestSession = await GetDB().Queryable<ChatSessionEntity>()
                    .Where(s => s.FeishuChatKey == feishuChatKey)
                    .OrderByDescending(s => s.UpdatedAt)
                    .FirstAsync();
                if (latestSession != null)
                {
                    await GetDB().Updateable<ChatSessionEntity>()
                        .SetColumns(s => s.IsFeishuActive == true)
                        .SetColumns(s => s.UpdatedAt == DateTime.Now)
                        .Where(s => s.SessionId == latestSession.SessionId)
                        .ExecuteCommandAsync();
                }
            }

            await GetDB().Ado.CommitTranAsync();
            return true;
        }
        catch
        {
            await GetDB().Ado.RollbackTranAsync();
            throw;
        }
    }

    /// <summary>
    /// 创建飞书新会话
    /// </summary>
    public async Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null, string? toolId = null)
    {
        try
        {
            // 使用事务
            await GetDB().Ado.BeginTranAsync();

            // 将该聊天下所有会话设为非活跃
            await GetDB().Updateable<ChatSessionEntity>()
                .SetColumns(s => s.IsFeishuActive == false)
                .Where(s => s.FeishuChatKey == feishuChatKey)
                .ExecuteCommandAsync();
            // 创建新会话
            var newSessionId = Guid.NewGuid().ToString();
            var newSession = new ChatSessionEntity
            {
                SessionId = newSessionId,
                Username = username,
                FeishuChatKey = feishuChatKey,
                IsFeishuActive = true,
                WorkspacePath = workspacePath,
                ToolId = toolId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            await GetDB().Insertable(newSession).ExecuteCommandAsync();

            await GetDB().Ado.CommitTranAsync();
            return newSessionId;
        }
        catch
        {
            await GetDB().Ado.RollbackTranAsync();
            throw;
        }
    }
}
