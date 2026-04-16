using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using System.Diagnostics;
using System.Reflection;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;
using WebCodeCli.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Tests;

public class CliExecutorServiceTests
{
    [Fact]
    public void GetCliThreadId_WhenPersistedThreadIdMissing_RecoversImportedCodexThreadIdFromTitle()
    {
        const string sessionId = "session-imported";
        const string cliThreadId = "019d1338-0c3f-7eb3-ae2b-e4617eb7d24e";

        var repository = new StubChatSessionRepository(
            [
                new ChatSessionEntity
                {
                    SessionId = sessionId,
                    Username = "luhaiyan",
                    ToolId = "codex",
                    Title = $"[Codex] {cliThreadId}",
                    WorkspacePath = @"D:\VSWorkshop\OpenDify",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                }
            ]);
        var serviceProvider = new NullServiceProvider(
            repository,
            new StubSessionOutputService());

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            Options.Create(new CliToolsOption
            {
                TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                Tools = []
            }),
            NullLogger<PersistentProcessManager>.Instance,
            serviceProvider,
            new StubChatSessionService(),
            new StubCliAdapterFactory());

        var resolvedThreadId = service.GetCliThreadId(sessionId);

        Assert.Equal(cliThreadId, resolvedThreadId);
        Assert.Equal(cliThreadId, repository.LastUpdatedCliThreadId);
        Assert.Equal(cliThreadId, repository.GetById(sessionId).CliThreadId);
    }

    [Fact]
    public void GetCliThreadId_WhenImportedTitleIsNotARealThreadId_DoesNotRecoverWrongValue()
    {
        const string sessionId = "session-imported-friendly-title";

        var repository = new StubChatSessionRepository(
            [
                new ChatSessionEntity
                {
                    SessionId = sessionId,
                    Username = "luhaiyan",
                    ToolId = "codex",
                    Title = "[Codex] 修复 OpenDify 登录问题",
                    WorkspacePath = @"D:\VSWorkshop\OpenDify",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                }
            ]);
        var serviceProvider = new NullServiceProvider(
            repository,
            new StubSessionOutputService());

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            Options.Create(new CliToolsOption
            {
                TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                Tools = []
            }),
            NullLogger<PersistentProcessManager>.Instance,
            serviceProvider,
            new StubChatSessionService(),
            new StubCliAdapterFactory());

        var resolvedThreadId = service.GetCliThreadId(sessionId);

        Assert.Null(resolvedThreadId);
        Assert.Null(repository.LastUpdatedCliThreadId);
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenProcessTimesOut_ReturnsTimeoutChunkInsteadOfThrowing()
    {
        var tool = new CliToolConfig
        {
            Id = "timeout-tool",
            Name = "Timeout Tool",
            Command = "powershell.exe",
            ArgumentTemplate = "-NoProfile -Command \"Start-Sleep -Seconds 3\"",
            TimeoutSeconds = 1,
            Enabled = true
        };

        var options = Options.Create(new CliToolsOption
        {
            TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
            Tools = [tool]
        });

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            options,
            NullLogger<PersistentProcessManager>.Instance,
            new NullServiceProvider(),
            new StubChatSessionService(),
            new StubCliAdapterFactory());

        var chunks = new List<StreamOutputChunk>();
        await foreach (var chunk in service.ExecuteStreamAsync("session-timeout", tool.Id, "ignored"))
        {
            chunks.Add(chunk);
        }

        var timeoutChunk = Assert.Single(chunks.Where(c => c.IsError && c.IsCompleted));
        Assert.Contains("执行超时", timeoutChunk.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenAdapterProvidesDetailedFailure_ReturnsUpstreamErrorMessage()
    {
        const string upstreamError = "unexpected status 402 Payment Required";

        var tool = new CliToolConfig
        {
            Id = "adapter-error-tool",
            Name = "Adapter Error Tool",
            Command = "powershell.exe",
            ArgumentTemplate = "-NoProfile -Command \"Write-Output 'RETRY: Reconnecting... 1/5'; Write-Output 'ERROR: unexpected status 402 Payment Required'; exit 1\"",
            TimeoutSeconds = 10,
            Enabled = true
        };

        var options = Options.Create(new CliToolsOption
        {
            TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
            Tools = [tool]
        });

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            options,
            NullLogger<PersistentProcessManager>.Instance,
            new NullServiceProvider(),
            new StubChatSessionService(),
            new StubCliAdapterFactory(new StubErrorAdapter()));

        var chunks = new List<StreamOutputChunk>();
        await foreach (var chunk in service.ExecuteStreamAsync("session-adapter-error", tool.Id, "ignored"))
        {
            chunks.Add(chunk);
        }

        var failureChunk = Assert.Single(chunks.Where(c => c.IsError && c.IsCompleted));
        Assert.Contains(upstreamError, failureChunk.ErrorMessage);
        Assert.DoesNotContain("执行失败（退出码", failureChunk.ErrorMessage);
    }

    [Fact]
    public void CodexAdapter_ParseOutputLine_WhenTurnFailed_SetsErrorMessage()
    {
        const string upstreamError = "unexpected status 402 Payment Required";
        var adapter = new CodexAdapter();

        var outputEvent = adapter.ParseOutputLine(
            """{"type":"turn.failed","error":{"message":"unexpected status 402 Payment Required","code":402}}""");

        var failureEvent = Assert.IsType<CliOutputEvent>(outputEvent);
        Assert.True(failureEvent.IsError);
        Assert.Equal(upstreamError, failureEvent.ErrorMessage);
    }

    [Fact]
    public void RewriteCodexLaunchToNode_WhenCommandIsCmdWrapper_RewritesToNodeJsEntry()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        var npmRoot = Path.Combine(tempRoot, "npm");
        var codexCmdPath = Path.Combine(npmRoot, "codex.cmd");
        var codexJsPath = Path.Combine(npmRoot, "node_modules", "@openai", "codex", "bin", "codex.js");
        var fakeNodePath = Path.Combine(tempRoot, "node.exe");

        Directory.CreateDirectory(Path.GetDirectoryName(codexJsPath)!);
        File.WriteAllText(codexCmdPath, "@echo off");
        File.WriteAllText(codexJsPath, "console.log('codex');");
        File.WriteAllText(fakeNodePath, string.Empty);

        try
        {
            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                Options.Create(new CliToolsOption
                {
                    TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                    Tools = []
                }),
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(),
                new StubChatSessionService(),
                new StubCliAdapterFactory());

            typeof(CliExecutorService)
                .GetField("_preferredNodeExecutablePath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, fakeNodePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = codexCmdPath,
                Arguments = "exec --json \"problematic \\\"prompt\\\"\""
            };

            typeof(CliExecutorService)
                .GetMethod("RewriteCodexLaunchToNode", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, [startInfo, new CliToolConfig { Id = "codex", Command = "codex" }, codexCmdPath, startInfo.Arguments]);

            Assert.Equal(fakeNodePath, startInfo.FileName);
            Assert.StartsWith($"\"{codexJsPath}\" ", startInfo.Arguments, StringComparison.Ordinal);
            Assert.Contains("problematic \\\"prompt\\\"", startInfo.Arguments, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveCommandPath_WhenCommandExistsOnWindowsPath_ReturnsFullPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        var pathEntry = Path.Combine(tempRoot, "bin");
        const string commandName = "webcode-path-only-test";
        var commandPath = Path.Combine(pathEntry, commandName + ".cmd");
        Directory.CreateDirectory(pathEntry);
        File.WriteAllText(commandPath, "@echo off");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");

        try
        {
            Environment.SetEnvironmentVariable("PATH", pathEntry);
            Environment.SetEnvironmentVariable("PATHEXT", ".COM;.EXE;.BAT;.CMD");

            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                Options.Create(new CliToolsOption
                {
                    TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                    Tools = []
                }),
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(),
                new StubChatSessionService(),
                new StubCliAdapterFactory());

            var resolvedCommand = (string)typeof(CliExecutorService)
                .GetMethod("ResolveCommandPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, [commandName])!;

            Assert.Equal(commandPath, resolvedCommand, ignoreCase: OperatingSystem.IsWindows());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("PATHEXT", originalPathExt);

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenCommandExistsAsWindowsCmdShimOnPath_StartsSuccessfully()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var shimDirectory = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shimDirectory);

        var shimPath = Path.Combine(shimDirectory, "fakecli.cmd");
        await File.WriteAllTextAsync(shimPath, "@ECHO off\r\necho shim-ok\r\n");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", shimDirectory + ";" + originalPath);

        try
        {
            var tool = new CliToolConfig
            {
                Id = "fakecli",
                Name = "Fake CLI",
                Command = "fakecli",
                ArgumentTemplate = "",
                TimeoutSeconds = 5,
                Enabled = true
            };

            var options = Options.Create(new CliToolsOption
            {
                TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
                Tools = [tool]
            });

            var service = new CliExecutorService(
                NullLogger<CliExecutorService>.Instance,
                options,
                NullLogger<PersistentProcessManager>.Instance,
                new NullServiceProvider(),
                new StubChatSessionService(),
                new StubCliAdapterFactory());

            var chunks = new List<StreamOutputChunk>();
            await foreach (var chunk in service.ExecuteStreamAsync("session-cmd-shim", tool.Id, "ignored"))
            {
                chunks.Add(chunk);
            }

            Assert.DoesNotContain(chunks, c => c.IsError && c.IsCompleted);
            Assert.Contains(chunks, c => c.Content.Contains("shim-ok", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);

            if (Directory.Exists(shimDirectory))
            {
                Directory.Delete(shimDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteStreamAsync_WhenWorkingDirectoryMissing_ReturnsFriendlyError()
    {
        var tool = new CliToolConfig
        {
            Id = "missing-working-dir-tool",
            Name = "Missing Working Dir Tool",
            Command = "powershell.exe",
            ArgumentTemplate = "-NoProfile -Command \"Write-Output 'should not start'\"",
            WorkingDirectory = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N"), "missing"),
            TimeoutSeconds = 10,
            Enabled = true
        };

        var options = Options.Create(new CliToolsOption
        {
            TempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "WebCodeCli.Tests", Guid.NewGuid().ToString("N")),
            Tools = [tool]
        });

        var service = new CliExecutorService(
            NullLogger<CliExecutorService>.Instance,
            options,
            NullLogger<PersistentProcessManager>.Instance,
            new NullServiceProvider(),
            new StubChatSessionService(),
            new StubCliAdapterFactory());

        var chunks = new List<StreamOutputChunk>();
        await foreach (var chunk in service.ExecuteStreamAsync("session-missing-working-dir", tool.Id, "ignored"))
        {
            chunks.Add(chunk);
        }

        var failureChunk = Assert.Single(chunks.Where(c => c.IsError && c.IsCompleted));
        Assert.Contains("工作目录不存在", failureChunk.ErrorMessage);
        Assert.Contains(tool.WorkingDirectory, failureChunk.ErrorMessage);
    }

    private sealed class StubChatSessionService : IChatSessionService
    {
        public void AddMessage(string sessionId, ChatMessage message) { }

        public List<ChatMessage> GetMessages(string sessionId) => [];

        public void ClearSession(string sessionId) { }

        public void UpdateMessage(string sessionId, string messageId, Action<ChatMessage> updateAction) { }

        public ChatMessage? GetMessage(string sessionId, string messageId) => null;
    }

    private sealed class StubCliAdapterFactory(params ICliToolAdapter[] adapters) : ICliAdapterFactory
    {
        private readonly ICliToolAdapter[] _adapters = adapters;

        public ICliToolAdapter? GetAdapter(CliToolConfig tool)
            => _adapters.FirstOrDefault(adapter => adapter.CanHandle(tool));

        public ICliToolAdapter? GetAdapter(string toolId)
            => _adapters.FirstOrDefault(adapter =>
                adapter.SupportedToolIds.Any(id => string.Equals(id, toolId, StringComparison.OrdinalIgnoreCase)));

        public bool SupportsStreamParsing(CliToolConfig tool)
            => GetAdapter(tool)?.SupportsStreamParsing ?? false;

        public IEnumerable<ICliToolAdapter> GetAllAdapters() => _adapters;
    }

    private sealed class StubErrorAdapter : ICliToolAdapter
    {
        public string[] SupportedToolIds => ["adapter-error-tool"];

        public bool SupportsStreamParsing => true;

        public bool CanHandle(CliToolConfig tool)
            => string.Equals(tool.Id, "adapter-error-tool", StringComparison.OrdinalIgnoreCase);

        public string BuildArguments(CliToolConfig tool, string prompt, CliSessionContext context)
            => tool.ArgumentTemplate;

        public CliOutputEvent? ParseOutputLine(string line)
        {
            if (line.StartsWith("RETRY: ", StringComparison.OrdinalIgnoreCase))
            {
                return new CliOutputEvent
                {
                    EventType = "error",
                    IsError = true,
                    ErrorMessage = line["RETRY: ".Length..]
                };
            }

            if (line.StartsWith("ERROR: ", StringComparison.OrdinalIgnoreCase))
            {
                return new CliOutputEvent
                {
                    EventType = "error",
                    IsError = true,
                    ErrorMessage = line["ERROR: ".Length..]
                };
            }

            return null;
        }

        public string? ExtractSessionId(CliOutputEvent outputEvent) => null;

        public string? ExtractAssistantMessage(CliOutputEvent outputEvent) => null;

        public string GetEventTitle(CliOutputEvent outputEvent) => outputEvent.Title ?? string.Empty;

        public string GetEventBadgeClass(CliOutputEvent outputEvent) => string.Empty;

        public string GetEventBadgeLabel(CliOutputEvent outputEvent) => string.Empty;
    }

    private sealed class StubSessionOutputService : ISessionOutputService
    {
        public Task<OutputPanelState?> GetBySessionIdAsync(string sessionId)
            => Task.FromResult<OutputPanelState?>(new OutputPanelState
            {
                SessionId = sessionId,
                ActiveThreadId = string.Empty
            });

        public Task<bool> SaveAsync(OutputPanelState state) => Task.FromResult(true);

        public Task<bool> DeleteBySessionIdAsync(string sessionId) => Task.FromResult(true);
    }

    private sealed class NullServiceProvider(
        IChatSessionRepository? chatSessionRepository = null,
        ISessionOutputService? sessionOutputService = null)
        : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            if (serviceType == typeof(IChatSessionRepository))
            {
                return chatSessionRepository;
            }

            if (serviceType == typeof(ISessionOutputService))
            {
                return sessionOutputService;
            }

            return null;
        }

        public IServiceScope CreateScope() => this;

        public IServiceProvider ServiceProvider => this;

        public void Dispose()
        {
        }
    }

    private sealed class StubChatSessionRepository(IEnumerable<ChatSessionEntity> sessions) : IChatSessionRepository
    {
        private readonly List<ChatSessionEntity> _sessions = sessions.ToList();

        public string? LastUpdatedCliThreadId { get; private set; }

        public SqlSugarScope GetDB() => throw new NotSupportedException();
        public List<ChatSessionEntity> GetList() => _sessions.ToList();
        public Task<List<ChatSessionEntity>> GetListAsync() => Task.FromResult(GetList());
        public List<ChatSessionEntity> GetList(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Where(whereExpression).ToList();
        public Task<List<ChatSessionEntity>> GetListAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetList(whereExpression));
        public int Count(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Count(whereExpression);
        public Task<int> CountAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(Count(whereExpression));
        public PageList<ChatSessionEntity> GetPageList(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public ChatSessionEntity GetById(dynamic id) => _sessions.First(x => string.Equals(x.SessionId, id?.ToString(), StringComparison.OrdinalIgnoreCase));
        public Task<ChatSessionEntity> GetByIdAsync(dynamic id) => Task.FromResult(GetById(id));
        public ChatSessionEntity GetSingle(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Single(whereExpression);
        public Task<ChatSessionEntity> GetSingleAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetSingle(whereExpression));
        public ChatSessionEntity GetFirst(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().First(whereExpression);
        public Task<ChatSessionEntity> GetFirstAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetFirst(whereExpression));
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
        public bool Delete(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => throw new NotSupportedException();
        public bool Update(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public bool UpdateRange(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public bool InsertOrUpdate(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> InsertOrUpdateAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> UpdateRangeAsync(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public bool IsAny(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.AsQueryable().Any(whereExpression);
        public Task<bool> IsAnyAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(IsAny(whereExpression));
        public Task<List<ChatSessionEntity>> GetByUsernameAsync(string username) => Task.FromResult(_sessions.Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<ChatSessionEntity?> GetByIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> DeleteByIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_sessions.RemoveAll(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)) > 0);
        public Task<List<ChatSessionEntity>> GetByUsernameOrderByUpdatedAtAsync(string username) => Task.FromResult(_sessions.Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.UpdatedAt).ToList());
        public Task<ChatSessionEntity?> GetByUsernameToolAndCliThreadIdAsync(string username, string toolId, string cliThreadId) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.CliThreadId, cliThreadId, StringComparison.OrdinalIgnoreCase)));
        public Task<ChatSessionEntity?> GetByToolAndCliThreadIdAsync(string toolId, string cliThreadId) => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.CliThreadId, cliThreadId, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> UpdateCliThreadIdAsync(string sessionId, string cliThreadId)
        {
            var session = _sessions.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            if (session == null)
            {
                return Task.FromResult(false);
            }

            session.CliThreadId = cliThreadId;
            session.UpdatedAt = DateTime.Now;
            LastUpdatedCliThreadId = cliThreadId;
            return Task.FromResult(true);
        }

        public Task<bool> UpdateWorkspaceBindingAsync(string sessionId, string? workspacePath, bool isCustomWorkspace) => Task.FromResult(true);
        public Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey) => Task.FromResult(new List<ChatSessionEntity>());
        public Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey) => Task.FromResult<ChatSessionEntity?>(null);
        public Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId) => Task.FromResult(true);
        public Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId) => Task.FromResult(true);
        public Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null, string? toolId = null) => Task.FromResult(Guid.NewGuid().ToString("N"));
    }
}
