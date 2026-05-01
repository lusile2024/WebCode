using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Model;
using WebCodeCli.Domain.Repositories.Base;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Tests;

public class SessionHistoryManagerTests
{
    [Fact]
    public async Task SaveSessionImmediateAsync_RoundTripsToolLaunchOverrides()
    {
        var sessionRepository = new InMemoryChatSessionRepository();
        var messageRepository = new InMemoryChatMessageRepository();
        var manager = new SessionHistoryManager(
            sessionRepository,
            messageRepository,
            new StubUserContextService(),
            NullLogger<SessionHistoryManager>.Instance);

        var session = new SessionHistory
        {
            SessionId = "session-launch-overrides-roundtrip",
            Title = "Launch overrides",
            WorkspacePath = @"D:\repo\superpowers",
            ToolId = "codex",
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = "continue",
                    CreatedAt = DateTime.UtcNow
                }
            ],
            ToolLaunchOverrides = new Dictionary<string, SessionToolLaunchOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["codex"] = new() { Model = "gpt-5.4", ReasoningEffort = "high" },
                ["claude-code"] = new() { Model = "sonnet" }
            }
        };

        await manager.SaveSessionImmediateAsync(session);
        manager.ClearCache();

        var storedEntity = await sessionRepository.GetByIdAndUsernameAsync(session.SessionId, "default");
        var reloadedSession = await manager.GetSessionAsync(session.SessionId);

        Assert.NotNull(storedEntity);
        Assert.Contains("\"codex\"", storedEntity!.ToolLaunchOverridesJson, StringComparison.Ordinal);
        Assert.Contains("\"reasoningEffort\":\"high\"", storedEntity.ToolLaunchOverridesJson, StringComparison.Ordinal);

        Assert.NotNull(reloadedSession);
        Assert.Equal("gpt-5.4", reloadedSession!.ToolLaunchOverrides["codex"].Model);
        Assert.Equal("high", reloadedSession.ToolLaunchOverrides["codex"].ReasoningEffort);
        Assert.Equal("sonnet", reloadedSession.ToolLaunchOverrides["claude-code"].Model);
    }

    [Fact]
    public async Task SaveSessionImmediateAsync_WhenOverridesEmpty_ClearsToolLaunchOverridesJson()
    {
        var sessionRepository = new InMemoryChatSessionRepository();
        var manager = new SessionHistoryManager(
            sessionRepository,
            new InMemoryChatMessageRepository(),
            new StubUserContextService(),
            NullLogger<SessionHistoryManager>.Instance);

        var session = new SessionHistory
        {
            SessionId = "session-empty-overrides",
            Title = "Launch overrides",
            WorkspacePath = @"D:\repo\superpowers",
            ToolId = "codex",
            Messages = [],
            ToolLaunchOverrides = new Dictionary<string, SessionToolLaunchOverride>(StringComparer.OrdinalIgnoreCase)
        };

        await manager.SaveSessionImmediateAsync(session);

        var storedEntity = await sessionRepository.GetByIdAndUsernameAsync(session.SessionId, "default");

        Assert.NotNull(storedEntity);
        Assert.Null(storedEntity!.ToolLaunchOverridesJson);
    }

    [Fact]
    public async Task GetSessionAsync_WhenStoredJsonIsInvalid_ReturnsEmptyOverrides()
    {
        var sessionRepository = new InMemoryChatSessionRepository(
        [
            new ChatSessionEntity
            {
                SessionId = "session-invalid-launch-overrides-json",
                Username = "default",
                Title = "Launch overrides",
                ToolId = "codex",
                WorkspacePath = @"D:\repo\superpowers",
                ToolLaunchOverridesJson = "{invalid json",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        ]);

        var manager = new SessionHistoryManager(
            sessionRepository,
            new InMemoryChatMessageRepository(),
            new StubUserContextService(),
            NullLogger<SessionHistoryManager>.Instance);

        var session = await manager.GetSessionAsync("session-invalid-launch-overrides-json");

        Assert.NotNull(session);
        Assert.Empty(session!.ToolLaunchOverrides);
    }

    private sealed class StubUserContextService : IUserContextService
    {
        private string _username = "default";

        public string GetCurrentUsername() => _username;

        public string GetCurrentRole() => "user";

        public bool IsAuthenticated() => true;

        public void SetCurrentUsername(string username)
        {
            _username = string.IsNullOrWhiteSpace(username) ? "default" : username.Trim();
        }
    }

    private sealed class InMemoryChatSessionRepository(IEnumerable<ChatSessionEntity>? initialSessions = null) : IChatSessionRepository
    {
        private readonly List<ChatSessionEntity> _sessions = initialSessions?.ToList() ?? [];

        public SqlSugarScope GetDB() => throw new NotSupportedException();
        public List<ChatSessionEntity> GetList() => [.. _sessions];
        public Task<List<ChatSessionEntity>> GetListAsync() => Task.FromResult(GetList());
        public List<ChatSessionEntity> GetList(Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Where(whereExpression).ToList();
        public Task<List<ChatSessionEntity>> GetListAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetList(whereExpression));
        public int Count(Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Count(whereExpression);
        public Task<int> CountAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(Count(whereExpression));
        public PageList<ChatSessionEntity> GetPageList(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public ChatSessionEntity GetById(dynamic id) => _sessions.First(x => string.Equals(x.SessionId, id?.ToString(), StringComparison.OrdinalIgnoreCase));
        public Task<ChatSessionEntity> GetByIdAsync(dynamic id) => Task.FromResult(GetById(id));
        public ChatSessionEntity GetSingle(Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Single(whereExpression);
        public Task<ChatSessionEntity> GetSingleAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetSingle(whereExpression));
        public ChatSessionEntity GetFirst(Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().First(whereExpression);
        public Task<ChatSessionEntity> GetFirstAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetFirst(whereExpression));
        public bool Insert(ChatSessionEntity obj) { _sessions.Add(obj); return true; }
        public Task<bool> InsertAsync(ChatSessionEntity obj) => Task.FromResult(Insert(obj));
        public bool InsertRange(List<ChatSessionEntity> objs) { _sessions.AddRange(objs); return true; }
        public Task<bool> InsertRangeAsync(List<ChatSessionEntity> objs) => Task.FromResult(InsertRange(objs));
        public int InsertReturnIdentity(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<int> InsertReturnIdentityAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public long InsertReturnBigIdentity(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<long> InsertReturnBigIdentityAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public bool DeleteByIds(dynamic[] ids) => throw new NotSupportedException();
        public Task<bool> DeleteByIdsAsync(dynamic[] ids) => throw new NotSupportedException();
        public bool Delete(dynamic id) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(dynamic id) => throw new NotSupportedException();
        public bool Delete(ChatSessionEntity obj) => _sessions.Remove(obj);
        public Task<bool> DeleteAsync(ChatSessionEntity obj) => Task.FromResult(Delete(obj));
        public bool Delete(Expression<Func<ChatSessionEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression) => throw new NotSupportedException();
        public bool Update(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public bool UpdateRange(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public Task<bool> UpdateRangeAsync(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public bool InsertOrUpdate(ChatSessionEntity obj)
        {
            var existing = _sessions.FirstOrDefault(x => string.Equals(x.SessionId, obj.SessionId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                _sessions.Add(obj);
                return true;
            }

            var index = _sessions.IndexOf(existing);
            _sessions[index] = obj;
            return true;
        }

        public Task<bool> InsertOrUpdateAsync(ChatSessionEntity obj) => Task.FromResult(InsertOrUpdate(obj));
        public bool IsAny(Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Any(whereExpression);
        public Task<bool> IsAnyAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(IsAny(whereExpression));
        public Task<List<ChatSessionEntity>> GetByUsernameAsync(string username) => Task.FromResult(_sessions.Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<ChatSessionEntity?> GetByIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> DeleteByIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_sessions.RemoveAll(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)) > 0);
        public Task<List<ChatSessionEntity>> GetByUsernameOrderByUpdatedAtAsync(string username) => Task.FromResult(_sessions.Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.UpdatedAt).ToList());
        public Task<ChatSessionEntity?> GetByUsernameToolAndCliThreadIdAsync(string username, string toolId, string cliThreadId) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.CliThreadId, cliThreadId, StringComparison.OrdinalIgnoreCase)));
        public Task<ChatSessionEntity?> GetByToolAndCliThreadIdAsync(string toolId, string cliThreadId) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.CliThreadId, cliThreadId, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> UpdateCliThreadIdAsync(string sessionId, string? cliThreadId)
        {
            var session = _sessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            if (session == null)
            {
                return Task.FromResult(false);
            }

            session.CliThreadId = cliThreadId;
            session.UpdatedAt = DateTime.Now;
            return Task.FromResult(true);
        }

        public Task<bool> UpdateWorkspaceBindingAsync(string sessionId, string? workspacePath, bool isCustomWorkspace) => Task.FromResult(true);
        public Task<bool> UpdateSessionTitleAsync(string sessionId, string title) => Task.FromResult(true);
        public Task<bool> UpdateCcSwitchSnapshotAsync(string sessionId, CcSwitchSessionSnapshot snapshot) => Task.FromResult(true);
        public Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey) => Task.FromResult(new List<ChatSessionEntity>());
        public Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey) => Task.FromResult<ChatSessionEntity?>(null);
        public Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId) => Task.FromResult(true);
        public Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId) => Task.FromResult(true);
        public Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null, string? toolId = null) => Task.FromResult(Guid.NewGuid().ToString("N"));
    }

    private sealed class InMemoryChatMessageRepository : IChatMessageRepository
    {
        private readonly List<ChatMessageEntity> _messages = [];

        public SqlSugarScope GetDB() => throw new NotSupportedException();
        public List<ChatMessageEntity> GetList() => [.. _messages];
        public Task<List<ChatMessageEntity>> GetListAsync() => Task.FromResult(GetList());
        public List<ChatMessageEntity> GetList(Expression<Func<ChatMessageEntity, bool>> whereExpression) => _messages.AsQueryable().Where(whereExpression).ToList();
        public Task<List<ChatMessageEntity>> GetListAsync(Expression<Func<ChatMessageEntity, bool>> whereExpression) => Task.FromResult(GetList(whereExpression));
        public int Count(Expression<Func<ChatMessageEntity, bool>> whereExpression) => _messages.AsQueryable().Count(whereExpression);
        public Task<int> CountAsync(Expression<Func<ChatMessageEntity, bool>> whereExpression) => Task.FromResult(Count(whereExpression));
        public PageList<ChatMessageEntity> GetPageList(Expression<Func<ChatMessageEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(Expression<Func<ChatMessageEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatMessageEntity>> GetPageListAsync(Expression<Func<ChatMessageEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(Expression<Func<ChatMessageEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<ChatMessageEntity> GetPageList(Expression<Func<ChatMessageEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatMessageEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatMessageEntity>> GetPageListAsync(Expression<Func<ChatMessageEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatMessageEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(Expression<Func<ChatMessageEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatMessageEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(Expression<Func<ChatMessageEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatMessageEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<ChatMessageEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatMessageEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public PageList<ChatMessageEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page, Expression<Func<ChatMessageEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatMessageEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page, Expression<Func<ChatMessageEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public ChatMessageEntity GetById(dynamic id) => throw new NotSupportedException();
        public Task<ChatMessageEntity> GetByIdAsync(dynamic id) => throw new NotSupportedException();
        public ChatMessageEntity GetSingle(Expression<Func<ChatMessageEntity, bool>> whereExpression) => _messages.AsQueryable().Single(whereExpression);
        public Task<ChatMessageEntity> GetSingleAsync(Expression<Func<ChatMessageEntity, bool>> whereExpression) => Task.FromResult(GetSingle(whereExpression));
        public ChatMessageEntity GetFirst(Expression<Func<ChatMessageEntity, bool>> whereExpression) => _messages.AsQueryable().First(whereExpression);
        public Task<ChatMessageEntity> GetFirstAsync(Expression<Func<ChatMessageEntity, bool>> whereExpression) => Task.FromResult(GetFirst(whereExpression));
        public bool Insert(ChatMessageEntity obj) { _messages.Add(obj); return true; }
        public Task<bool> InsertAsync(ChatMessageEntity obj) => Task.FromResult(Insert(obj));
        public bool InsertRange(List<ChatMessageEntity> objs) { _messages.AddRange(objs); return true; }
        public Task<bool> InsertRangeAsync(List<ChatMessageEntity> objs) => Task.FromResult(InsertRange(objs));
        public int InsertReturnIdentity(ChatMessageEntity obj) => throw new NotSupportedException();
        public Task<int> InsertReturnIdentityAsync(ChatMessageEntity obj) => throw new NotSupportedException();
        public long InsertReturnBigIdentity(ChatMessageEntity obj) => throw new NotSupportedException();
        public Task<long> InsertReturnBigIdentityAsync(ChatMessageEntity obj) => throw new NotSupportedException();
        public bool DeleteByIds(dynamic[] ids) => throw new NotSupportedException();
        public Task<bool> DeleteByIdsAsync(dynamic[] ids) => throw new NotSupportedException();
        public bool Delete(dynamic id) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(dynamic id) => throw new NotSupportedException();
        public bool Delete(ChatMessageEntity obj) => _messages.Remove(obj);
        public Task<bool> DeleteAsync(ChatMessageEntity obj) => Task.FromResult(Delete(obj));
        public bool Delete(Expression<Func<ChatMessageEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Expression<Func<ChatMessageEntity, bool>> whereExpression) => throw new NotSupportedException();
        public bool Update(ChatMessageEntity obj) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(ChatMessageEntity obj) => throw new NotSupportedException();
        public bool UpdateRange(List<ChatMessageEntity> objs) => throw new NotSupportedException();
        public Task<bool> UpdateRangeAsync(List<ChatMessageEntity> objs) => throw new NotSupportedException();
        public bool InsertOrUpdate(ChatMessageEntity obj) => throw new NotSupportedException();
        public Task<bool> InsertOrUpdateAsync(ChatMessageEntity obj) => throw new NotSupportedException();
        public bool IsAny(Expression<Func<ChatMessageEntity, bool>> whereExpression) => _messages.AsQueryable().Any(whereExpression);
        public Task<bool> IsAnyAsync(Expression<Func<ChatMessageEntity, bool>> whereExpression) => Task.FromResult(IsAny(whereExpression));
        public Task<List<ChatMessageEntity>> GetBySessionIdAsync(string sessionId) => Task.FromResult(_messages.Where(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<List<ChatMessageEntity>> GetBySessionIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_messages.Where(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<bool> DeleteBySessionIdAsync(string sessionId)
        {
            _messages.RemoveAll(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(true);
        }

        public Task<bool> DeleteBySessionIdAndUsernameAsync(string sessionId, string username)
        {
            _messages.RemoveAll(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(true);
        }

        public Task<bool> InsertMessagesAsync(List<ChatMessageEntity> messages)
        {
            _messages.AddRange(messages);
            return Task.FromResult(true);
        }
    }
}
