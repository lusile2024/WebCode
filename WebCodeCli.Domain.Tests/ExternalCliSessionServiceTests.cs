using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Tests;

public class ExternalCliSessionServiceTests
{
    [Fact]
    public async Task DiscoverAsync_ForClaudeCode_MergesJsonlWhenIndexExists_AndKeepsLatestSessionVisible()
    {
        using var sandbox = new ExternalCliSessionSandbox();

        var workspaceRoot = sandbox.CreateDirectory("workspaces");
        var indexedWorkspace = sandbox.CreateDirectory(Path.Combine("workspaces", "indexed"));
        var latestWorkspace = sandbox.CreateDirectory(Path.Combine("workspaces", "latest"));
        var userProfile = sandbox.CreateDirectory("userprofile");

        var indexedProjectPath = sandbox.CreateDirectory(Path.Combine("userprofile", ".claude", "projects", "indexed-project"));
        var indexedSessionId = Guid.NewGuid().ToString();
        sandbox.WriteFile(
            Path.Combine("userprofile", ".claude", "projects", "indexed-project", "sessions-index.json"),
            JsonSerializer.Serialize(new
            {
                version = 1,
                entries = new[]
                {
                    new
                    {
                        sessionId = indexedSessionId,
                        projectPath = indexedWorkspace,
                        modified = "2026-03-23T00:01:00Z"
                    }
                }
            }));

        var latestSessionId = Guid.NewGuid().ToString();
        var latestTranscriptPath = sandbox.WriteFile(
            Path.Combine("userprofile", ".claude", "projects", "latest-project", $"{latestSessionId}.jsonl"),
            string.Join(
                "\n",
                JsonSerializer.Serialize(new
                {
                    type = "queue-operation",
                    operation = "enqueue",
                    timestamp = "2026-03-23T01:41:25.091Z",
                    sessionId = latestSessionId,
                    content = "test"
                }),
                JsonSerializer.Serialize(new
                {
                    type = "progress",
                    timestamp = "2026-03-23T01:40:49.184Z",
                    sessionId = latestSessionId,
                    cwd = latestWorkspace
                })));

        File.SetLastWriteTime(latestTranscriptPath, new DateTime(2026, 3, 23, 10, 6, 33));
        Directory.SetLastWriteTime(Path.GetDirectoryName(latestTranscriptPath)!, new DateTime(2026, 3, 23, 9, 54, 45));
        Directory.SetLastWriteTime(indexedProjectPath, new DateTime(2026, 3, 23, 8, 0, 0));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:AllowedRoots:0"] = workspaceRoot
            })
            .Build();

        var service = new TestExternalCliSessionService(
            userProfile,
            new StubChatSessionRepository([]),
            new StubUserWorkspacePolicyService(),
            configuration);

        var discovered = await service.DiscoverAsync("luhaiyan", "claude-code", maxCount: 100);

        Assert.Equal(2, discovered.Count);
        Assert.Equal(latestSessionId, discovered[0].CliThreadId);
        Assert.Equal(latestWorkspace, discovered[0].WorkspacePath);
        Assert.Equal(indexedSessionId, discovered[1].CliThreadId);
        Assert.Equal(indexedWorkspace, discovered[1].WorkspacePath);
    }

    private sealed class TestExternalCliSessionService(
        string userProfilePath,
        IChatSessionRepository chatSessionRepository,
        IUserWorkspacePolicyService userWorkspacePolicyService,
        IConfiguration configuration)
        : ExternalCliSessionService(
            NullLogger<ExternalCliSessionService>.Instance,
            chatSessionRepository,
            userWorkspacePolicyService,
            configuration)
    {
        protected override string? GetUserProfilePath() => userProfilePath;
    }

    private sealed class StubUserWorkspacePolicyService : IUserWorkspacePolicyService
    {
        public Task<List<string>> GetAllowedDirectoriesAsync(string username)
            => Task.FromResult(new List<string>());

        public Task<bool> IsPathAllowedAsync(string username, string directoryPath)
            => Task.FromResult(true);

        public Task<bool> SaveAllowedDirectoriesAsync(string username, IEnumerable<string> allowedDirectories)
            => Task.FromResult(true);
    }

    private sealed class StubChatSessionRepository(IEnumerable<ChatSessionEntity> sessions) : IChatSessionRepository
    {
        private readonly List<ChatSessionEntity> _sessions = sessions.ToList();

        public SqlSugarScope GetDB() => throw new NotSupportedException();
        public List<ChatSessionEntity> GetList() => _sessions.ToList();
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
        public Task<ChatSessionEntity> GetByIdAsync(dynamic id) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.SessionId, id?.ToString(), StringComparison.OrdinalIgnoreCase)))!;
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
        public bool InsertOrUpdate(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> InsertOrUpdateAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> UpdateRangeAsync(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public bool IsAny(Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Any(whereExpression);
        public Task<bool> IsAnyAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(IsAny(whereExpression));
        public List<ChatSessionEntity> GetByUsername(string username) => _sessions.Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).ToList();
        public Task<List<ChatSessionEntity>> GetByUsernameAsync(string username) => Task.FromResult(GetByUsername(username));
        public Task<ChatSessionEntity?> GetByIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> DeleteByIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_sessions.RemoveAll(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)) > 0);
        public Task<List<ChatSessionEntity>> GetByUsernameOrderByUpdatedAtAsync(string username) => Task.FromResult(_sessions.Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.UpdatedAt).ToList());
        public Task<ChatSessionEntity?> GetByUsernameToolAndCliThreadIdAsync(string username, string toolId, string cliThreadId) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.CliThreadId, cliThreadId, StringComparison.OrdinalIgnoreCase)));
        public Task<ChatSessionEntity?> GetByToolAndCliThreadIdAsync(string toolId, string cliThreadId) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.CliThreadId, cliThreadId, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> UpdateCliThreadIdAsync(string sessionId, string cliThreadId) => Task.FromResult(true);
        public Task<bool> UpdateWorkspaceBindingAsync(string sessionId, string? workspacePath, bool isCustomWorkspace) => Task.FromResult(true);
        public Task<bool> UpdateCcSwitchSnapshotAsync(string sessionId, CcSwitchSessionSnapshot snapshot) => Task.FromResult(true);
        public Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey) => Task.FromResult(_sessions.Where(x => string.Equals(x.FeishuChatKey, feishuChatKey, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.FeishuChatKey, feishuChatKey, StringComparison.OrdinalIgnoreCase) && x.IsFeishuActive));
        public Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId) => Task.FromResult(true);
        public Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId) => Task.FromResult(true);
        public Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null, string? toolId = null) => Task.FromResult(Guid.NewGuid().ToString("N"));
    }

    private sealed class ExternalCliSessionSandbox : IDisposable
    {
        public ExternalCliSessionSandbox()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "WebCodeCli.Domain.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string WriteFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content.Replace("\r\n", "\n"));
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
