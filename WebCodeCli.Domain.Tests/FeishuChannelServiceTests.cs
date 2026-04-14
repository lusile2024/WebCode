using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;
using WebCodeCli.Domain.Domain.Service.Channels;
using WebCodeCli.Domain.Repositories.Base;
using WebCodeCli.Domain.Repositories.Base.ChatSession;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Tests;

public class FeishuChannelServiceTests
{
    [Fact]
    public void CreateNewSession_WithCustomWorkspace_PersistsWorkspacePath()
    {
        var repository = CreateRepository(out var repositoryProxy);
        var sessionDirectoryService = new RecordingSessionDirectoryService(repositoryProxy);
        var serviceProvider = new TestServiceProvider(
            repository,
            sessionDirectoryService,
            new StubFeishuUserBindingService(),
            new StubUserFeishuBotConfigService(),
            new StubUserContextService());

        var service = new FeishuChannelService(
            Options.Create(new FeishuOptions
            {
                Enabled = true,
                AppId = "cli_test",
                AppSecret = "secret"
            }),
            NullLogger<FeishuChannelService>.Instance,
            CreateProxy<IFeishuCardKitClient>(),
            serviceProvider,
            CreateProxy<ICliExecutorService>(),
            CreateProxy<IChatSessionService>());

        var workspacePath = Path.Combine(Path.GetTempPath(), $"feishu-custom-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);

        try
        {
            var sessionId = service.CreateNewSession(
                new FeishuIncomingMessage
                {
                    ChatId = "oc_custom_workspace_chat",
                    SenderName = "luhaiyan"
                },
                workspacePath,
                "codex");

            var stored = repositoryProxy.GetStored(sessionId);

            Assert.Equal(workspacePath, stored.WorkspacePath);
            Assert.True(stored.IsCustomWorkspace);
            Assert.Equal("codex", stored.ToolId);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task SendMessageAsync_UsesPlainTextTransport()
    {
        var cardKit = new RecordingFeishuCardKitClient();
        var service = CreateService(cardKit);

        await service.SendMessageAsync("oc_text_chat", "已完成", "luhaiyan", "cli_text_bot");

        Assert.Equal(0, cardKit.CreateCardCallCount);
        Assert.Equal(0, cardKit.SendCardCallCount);
        Assert.Equal(1, cardKit.SendTextCallCount);
    }

    [Fact]
    public async Task ReplyMessageAsync_UsesPlainTextTransport()
    {
        var cardKit = new RecordingFeishuCardKitClient();
        var service = CreateService(cardKit);

        await service.ReplyMessageAsync("om_text_reply", "已完成", "luhaiyan", "cli_text_bot");

        Assert.Equal(0, cardKit.CreateCardCallCount);
        Assert.Equal(0, cardKit.ReplyCardCallCount);
        Assert.Equal(1, cardKit.ReplyTextCallCount);
    }

    [Fact]
    public async Task HandleIncomingMessageAsync_SupersedesPreviousExecutionAndReusesCliThreadId()
    {
        var repository = CreateRepository(out var repositoryProxy);
        var sessionDirectoryService = new RecordingSessionDirectoryService(repositoryProxy);
        var cardKit = new StreamingRecordingFeishuCardKitClient();
        var chatSessionService = new RecordingChatSessionService();
        var workspacePath = Path.Combine(Path.GetTempPath(), $"feishu-takeover-session-{Guid.NewGuid():N}", "superpowers");
        var cliExecutor = new TakeoverCliExecutor(workspacePath);
        var serviceProvider = new TestServiceProvider(
            repository,
            sessionDirectoryService,
            new StubFeishuUserBindingService(),
            new StubUserFeishuBotConfigService(),
            new StubUserContextService());

        var service = new FeishuChannelService(
            Options.Create(new FeishuOptions
            {
                Enabled = true,
                AppId = "cli_test",
                AppSecret = "secret"
            }),
            NullLogger<FeishuChannelService>.Instance,
            cardKit,
            serviceProvider,
            cliExecutor,
            chatSessionService);

        Directory.CreateDirectory(workspacePath);

        try
        {
            var sessionId = service.CreateNewSession(
                new FeishuIncomingMessage
                {
                    ChatId = "oc_takeover_chat",
                    SenderName = "luhaiyan"
                },
                workspacePath,
                "codex");

            var firstTask = service.HandleIncomingMessageAsync(new FeishuIncomingMessage
            {
                ChatId = "oc_takeover_chat",
                SenderName = "luhaiyan",
                MessageId = "msg-1",
                Content = "先查一下 superpowers 计划文件"
            });

            await cliExecutor.ThreadIdPersisted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var secondTask = service.HandleIncomingMessageAsync(new FeishuIncomingMessage
            {
                ChatId = "oc_takeover_chat",
                SenderName = "luhaiyan",
                MessageId = "msg-2",
                Content = "补充：D:\\MMIS\\Base\\Docs\\superpowers"
            });

            await Task.WhenAll(firstTask, secondTask);

            Assert.Collection(cliExecutor.ExecuteCalls,
                firstCall => Assert.Null(firstCall.ThreadIdAtStart),
                secondCall => Assert.Equal("thread-1", secondCall.ThreadIdAtStart));

            Assert.Equal("thread-1", cliExecutor.GetCliThreadId(sessionId));
            Assert.Null(cardKit.Handles[0].ReplyMessageId);
            Assert.Equal("当前回复已停止：同一会话收到了新的补充消息，请查看新卡片继续结果。", cardKit.Handles[0].FinalContent);
            Assert.Equal("补充完成", cardKit.Handles[1].FinalContent);
            Assert.Contains("已停止", cardKit.Handles[0].FinalStatusMarkdown);
            Assert.Contains("已完成", cardKit.Handles[1].FinalStatusMarkdown);
            Assert.Equal(1, cardKit.ReplyTextCallCount);
            Assert.Equal($"当前会话：superpowers  {sessionId[..8]}\n已完成", cardKit.LastReplyTextContent);
            Assert.Contains(chatSessionService.Messages[sessionId], message => message.Role == "user" && message.Content.Contains("补充：D:\\MMIS\\Base\\Docs\\superpowers", StringComparison.Ordinal));
            Assert.Contains(chatSessionService.Messages[sessionId], message => message.Role == "assistant" && message.Content == "补充完成");
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task HandleIncomingMessageAsync_ForwardsRawPromptWithoutReplyPrefixInstructions()
    {
        var repository = CreateRepository(out var repositoryProxy);
        var sessionDirectoryService = new RecordingSessionDirectoryService(repositoryProxy);
        var cardKit = new StreamingRecordingFeishuCardKitClient();
        var chatSessionService = new RecordingChatSessionService();
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"feishu-reply-prefix-{Guid.NewGuid():N}");
        var workspacePath = Path.Combine(workspaceRoot, "superpowers");
        Directory.CreateDirectory(workspacePath);
        var cliExecutor = new PromptCapturingCliExecutor(workspacePath);
        var serviceProvider = new TestServiceProvider(
            repository,
            sessionDirectoryService,
            new StubFeishuUserBindingService(),
            new StubUserFeishuBotConfigService(),
            new StubUserContextService());

        var service = new FeishuChannelService(
            Options.Create(new FeishuOptions
            {
                Enabled = true,
                AppId = "cli_test",
                AppSecret = "secret"
            }),
            NullLogger<FeishuChannelService>.Instance,
            cardKit,
            serviceProvider,
            cliExecutor,
            chatSessionService);

        try
        {
            var sessionId = service.CreateNewSession(
                new FeishuIncomingMessage
                {
                    ChatId = "oc_reply_prefix_chat",
                    SenderName = "luhaiyan"
                },
                workspacePath,
                "codex");

            await service.HandleIncomingMessageAsync(new FeishuIncomingMessage
            {
                ChatId = "oc_reply_prefix_chat",
                SenderName = "luhaiyan",
                MessageId = "msg-prefix",
                Content = @"D:\MMIS\Base\Docs\superpowers"
            });

            var call = Assert.Single(cliExecutor.ExecuteCalls);
            Assert.Equal(@"D:\MMIS\Base\Docs\superpowers", call.Prompt);
            var handle = Assert.Single(cardKit.Handles);
            Assert.Null(handle.ReplyMessageId);
            Assert.Equal("思考中...", handle.InitialContent);
            Assert.Equal("补充完成", handle.FinalContent);
            Assert.Contains("处理中", handle.InitialStatusMarkdown);
            Assert.Contains("／", handle.InitialStatusMarkdown);
            Assert.Contains("已完成", handle.FinalStatusMarkdown);
            Assert.Equal(1, cardKit.ReplyTextCallCount);
            Assert.Equal($"当前会话：superpowers  {sessionId[..8]}\n已完成", cardKit.LastReplyTextContent);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task HandleIncomingMessageAsync_CreatesStreamingCardWithSessionOverflowMenu()
    {
        var repository = CreateRepository(out var repositoryProxy);
        var sessionDirectoryService = new RecordingSessionDirectoryService(repositoryProxy);
        var cardKit = new StreamingRecordingFeishuCardKitClient();
        var chatSessionService = new RecordingChatSessionService();
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"feishu-streaming-menu-{Guid.NewGuid():N}");
        var currentWorkspace = Path.Combine(workspaceRoot, "superpowers");
        var otherWorkspace = Path.Combine(workspaceRoot, "backend");
        Directory.CreateDirectory(currentWorkspace);
        Directory.CreateDirectory(otherWorkspace);

        repositoryProxy.Store(new ChatSessionEntity
        {
            SessionId = "11111111-current",
            Username = "luhaiyan",
            WorkspacePath = currentWorkspace,
            ToolId = "codex",
            FeishuChatKey = "oc_menu_chat",
            IsFeishuActive = true,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTime.UtcNow
        });

        repositoryProxy.Store(new ChatSessionEntity
        {
            SessionId = "22222222-other",
            Username = "luhaiyan",
            WorkspacePath = otherWorkspace,
            ToolId = "claude-code",
            FeishuChatKey = "oc_menu_chat",
            IsFeishuActive = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-60),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        var cliExecutor = new PromptCapturingCliExecutor(currentWorkspace);
        var serviceProvider = new TestServiceProvider(
            repository,
            sessionDirectoryService,
            new StubFeishuUserBindingService(),
            new StubUserFeishuBotConfigService(),
            new StubUserContextService());

        var service = new FeishuChannelService(
            Options.Create(new FeishuOptions
            {
                Enabled = true,
                AppId = "cli_test",
                AppSecret = "secret"
            }),
            NullLogger<FeishuChannelService>.Instance,
            cardKit,
            serviceProvider,
            cliExecutor,
            chatSessionService);

        try
        {
            await service.HandleIncomingMessageAsync(new FeishuIncomingMessage
            {
                ChatId = "oc_menu_chat",
                SenderName = "luhaiyan",
                MessageId = "msg-menu",
                Content = "继续处理"
            });

            var handle = Assert.Single(cardKit.Handles);
            Assert.NotNull(handle.Chrome);
            var chrome = handle.Chrome!;
            Assert.Contains("当前会话", chrome.StatusMarkdown);
            Assert.Contains("superpowers", chrome.StatusMarkdown);
            Assert.Contains("11111111", chrome.StatusMarkdown);
            Assert.Contains(chrome.OverflowOptions, option => option.Text.Contains("backend", StringComparison.Ordinal));
            Assert.Contains(chrome.OverflowOptions, option => option.Text == "更多会话...");

            var switchOption = Assert.Single(chrome.OverflowOptions, option => option.Text.Contains("backend", StringComparison.Ordinal));
            var valueJson = JsonSerializer.Serialize(switchOption.Value);
            Assert.Contains("\"action\":\"switch_session\"", valueJson);
            Assert.Contains("\"session_id\":\"22222222-other\"", valueJson);
            Assert.Contains("\"chat_key\":\"oc_menu_chat\"", valueJson);

            var moreOption = Assert.Single(chrome.OverflowOptions, option => option.Text == "更多会话...");
            var moreValueJson = JsonSerializer.Serialize(moreOption.Value);
            Assert.Contains("\"action\":\"open_session_manager\"", moreValueJson);
            Assert.Contains("\"send_as_new_card\":true", moreValueJson);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static FeishuChannelService CreateService(IFeishuCardKitClient? cardKit = null)
    {
        var repository = CreateRepository(out _);
        var sessionDirectoryService = new RecordingSessionDirectoryService(new ChatSessionRepositoryProxy());
        var serviceProvider = new TestServiceProvider(
            repository,
            sessionDirectoryService,
            new StubFeishuUserBindingService(),
            new StubUserFeishuBotConfigService(),
            new StubUserContextService());

        return new FeishuChannelService(
            Options.Create(new FeishuOptions
            {
                Enabled = true,
                AppId = "cli_test",
                AppSecret = "secret"
            }),
            NullLogger<FeishuChannelService>.Instance,
            cardKit ?? new RecordingFeishuCardKitClient(),
            serviceProvider,
            CreateProxy<ICliExecutorService>(),
            CreateProxy<IChatSessionService>());
    }

    private static IChatSessionRepository CreateRepository(out ChatSessionRepositoryProxy proxy)
    {
        var repository = DispatchProxy.Create<IChatSessionRepository, ChatSessionRepositoryProxy>();
        proxy = (ChatSessionRepositoryProxy)(object)repository;
        return repository;
    }

    private static T CreateProxy<T>() where T : class
    {
        return DispatchProxy.Create<T, DefaultInterfaceProxy<T>>();
    }

    private sealed class TestServiceProvider(
        IChatSessionRepository chatSessionRepository,
        ISessionDirectoryService sessionDirectoryService,
        IFeishuUserBindingService feishuUserBindingService,
        IUserFeishuBotConfigService userFeishuBotConfigService,
        IUserContextService userContextService) : IServiceProvider, IServiceScopeFactory, IServiceScope
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

            if (serviceType == typeof(ISessionDirectoryService))
            {
                return sessionDirectoryService;
            }

            if (serviceType == typeof(IFeishuUserBindingService))
            {
                return feishuUserBindingService;
            }

            if (serviceType == typeof(IUserFeishuBotConfigService))
            {
                return userFeishuBotConfigService;
            }

            if (serviceType == typeof(IUserContextService))
            {
                return userContextService;
            }

            return null;
        }

        public IServiceScope CreateScope() => this;

        public IServiceProvider ServiceProvider => this;

        public void Dispose()
        {
        }
    }

    private sealed class StubUserContextService : IUserContextService
    {
        public string GetCurrentUsername() => "luhaiyan";

        public string GetCurrentRole() => "admin";

        public bool IsAuthenticated() => true;

        public void SetCurrentUsername(string username)
        {
        }
    }

    private sealed class StubFeishuUserBindingService : IFeishuUserBindingService
    {
        public Task<string?> GetBoundWebUsernameAsync(string feishuUserId) => Task.FromResult<string?>(null);

        public Task<bool> IsBoundAsync(string feishuUserId) => Task.FromResult(true);

        public Task<(bool Success, string? ErrorMessage, string? WebUsername)> BindAsync(string feishuUserId, string webUsername, string? appId = null)
            => Task.FromResult<(bool Success, string? ErrorMessage, string? WebUsername)>((true, null, webUsername));

        public Task<bool> UnbindAsync(string feishuUserId) => Task.FromResult(true);

        public Task<List<string>> GetBindableWebUsernamesAsync(string? appId = null)
            => Task.FromResult(new List<string> { "luhaiyan" });

        public Task<HashSet<string>> GetAllBoundWebUsernamesAsync()
            => Task.FromResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "luhaiyan" });
    }

    private sealed class StubUserFeishuBotConfigService : IUserFeishuBotConfigService
    {
        public Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username) => Task.FromResult<UserFeishuBotConfigEntity?>(null);

        public Task<UserFeishuBotConfigEntity?> GetByAppIdAsync(string appId) => Task.FromResult<UserFeishuBotConfigEntity?>(null);

        public Task<UserFeishuBotConfigSaveResult> SaveAsync(UserFeishuBotConfigEntity config) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string username) => Task.FromResult(true);

        public Task<string?> FindConflictingUsernameByAppIdAsync(string username, string? appId) => Task.FromResult<string?>(null);

        public Task<List<UserFeishuBotConfigEntity>> GetAutoStartCandidatesAsync()
            => Task.FromResult(new List<UserFeishuBotConfigEntity>());

        public Task<bool> UpdateRuntimePreferenceAsync(string username, bool autoStartEnabled, DateTime? lastStartedAt = null)
            => Task.FromResult(true);

        public FeishuOptions GetSharedDefaults() => new()
        {
            Enabled = true,
            AppId = "shared-app-id",
            AppSecret = "shared-secret",
            DefaultCardTitle = "AI助手",
            ThinkingMessage = "思考中..."
        };

        public Task<FeishuOptions> GetEffectiveOptionsAsync(string? username) => Task.FromResult(GetSharedDefaults());

        public Task<FeishuOptions?> GetEffectiveOptionsByAppIdAsync(string? appId)
            => Task.FromResult<FeishuOptions?>(string.IsNullOrWhiteSpace(appId)
                ? null
                : new FeishuOptions
                {
                    Enabled = true,
                    AppId = appId,
                    AppSecret = "bot-secret",
                    DefaultCardTitle = "AI助手",
                    ThinkingMessage = "思考中..."
                });
    }

    private sealed class RecordingFeishuCardKitClient : IFeishuCardKitClient
    {
        public int CreateCardCallCount { get; private set; }

        public int SendCardCallCount { get; private set; }

        public int ReplyCardCallCount { get; private set; }

        public int SendTextCallCount { get; private set; }

        public int ReplyTextCallCount { get; private set; }

        public Task<string> CreateCardAsync(string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            CreateCardCallCount++;
            return Task.FromResult("card-1");
        }

        public Task<bool> UpdateCardAsync(string cardId, string content, int sequence, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => Task.FromResult(true);

        public Task<string> SendCardMessageAsync(string chatId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            SendCardCallCount++;
            return Task.FromResult("message-card");
        }

        public Task<string> SendTextMessageAsync(string chatId, string content, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            SendTextCallCount++;
            return Task.FromResult("message-text");
        }

        public Task<string> ReplyCardMessageAsync(string replyMessageId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            ReplyCardCallCount++;
            return Task.FromResult("reply-card");
        }

        public Task<string> ReplyTextMessageAsync(string replyMessageId, string content, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            ReplyTextCallCount++;
            return Task.FromResult("reply-text");
        }

        public Task<FeishuStreamingHandle> CreateStreamingHandleAsync(string chatId, string? replyMessageId, string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null, FeishuStreamingCardChrome? chrome = null)
            => throw new NotSupportedException();

        public Task<string> SendRawCardAsync(string chatId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> ReplyElementsCardAsync(string replyMessageId, FeishuNetSdk.Im.Dtos.ElementsCardV2Dto card, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> ReplyRawCardAsync(string replyMessageId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();
    }

    private sealed class StreamingRecordingFeishuCardKitClient : IFeishuCardKitClient
    {
        public List<StreamingHandleRecord> Handles { get; } = new();

        public int ReplyTextCallCount { get; private set; }

        public string? LastReplyTextContent { get; private set; }

        public Task<string> CreateCardAsync(string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => Task.FromResult($"card-{Handles.Count + 1}");

        public Task<bool> UpdateCardAsync(string cardId, string content, int sequence, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => Task.FromResult(true);

        public Task<string> SendCardMessageAsync(string chatId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => Task.FromResult($"message-{cardId}");

        public Task<string> SendTextMessageAsync(string chatId, string content, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => Task.FromResult("message-text");

        public Task<string> ReplyCardMessageAsync(string replyMessageId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => Task.FromResult($"reply-{cardId}");

        public Task<string> ReplyTextMessageAsync(string replyMessageId, string content, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            ReplyTextCallCount++;
            LastReplyTextContent = content;
            return Task.FromResult($"reply-text-{ReplyTextCallCount}");
        }

        public Task<FeishuStreamingHandle> CreateStreamingHandleAsync(string chatId, string? replyMessageId, string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null, FeishuStreamingCardChrome? chrome = null)
        {
            var record = new StreamingHandleRecord
            {
                CardId = $"card-{Handles.Count + 1}",
                MessageId = $"message-{Handles.Count + 1}",
                ReplyMessageId = replyMessageId,
                InitialContent = initialContent,
                Chrome = chrome,
                InitialStatusMarkdown = chrome?.StatusMarkdown
            };
            if (!string.IsNullOrWhiteSpace(record.InitialStatusMarkdown))
            {
                record.StatusMarkdownSnapshots.Add(record.InitialStatusMarkdown);
            }
            Handles.Add(record);

            return Task.FromResult(new FeishuStreamingHandle(
                record.CardId,
                record.MessageId,
                content =>
                {
                    record.Updates.Add(content);
                    if (!string.IsNullOrWhiteSpace(chrome?.StatusMarkdown))
                    {
                        record.StatusMarkdownSnapshots.Add(chrome.StatusMarkdown);
                    }
                    return Task.CompletedTask;
                },
                content =>
                {
                    record.FinalContent = content;
                    record.FinalStatusMarkdown = chrome?.StatusMarkdown;
                    if (!string.IsNullOrWhiteSpace(record.FinalStatusMarkdown))
                    {
                        record.StatusMarkdownSnapshots.Add(record.FinalStatusMarkdown);
                    }
                    return Task.CompletedTask;
                },
                throttleMs: 0));
        }

        public Task<string> SendRawCardAsync(string chatId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> ReplyElementsCardAsync(string replyMessageId, FeishuNetSdk.Im.Dtos.ElementsCardV2Dto card, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> ReplyRawCardAsync(string replyMessageId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();
    }

    private sealed class StreamingHandleRecord
    {
        public string CardId { get; set; } = string.Empty;

        public string MessageId { get; set; } = string.Empty;

        public string? ReplyMessageId { get; set; }

        public string InitialContent { get; set; } = string.Empty;

        public string? InitialStatusMarkdown { get; set; }

        public List<string> Updates { get; } = new();

        public string? FinalContent { get; set; }

        public string? FinalStatusMarkdown { get; set; }

        public List<string> StatusMarkdownSnapshots { get; } = new();

        public FeishuStreamingCardChrome? Chrome { get; set; }
    }

    private sealed class RecordingChatSessionService : IChatSessionService
    {
        public Dictionary<string, List<ChatMessage>> Messages { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddMessage(string sessionId, ChatMessage message)
        {
            if (!Messages.TryGetValue(sessionId, out var sessionMessages))
            {
                sessionMessages = new List<ChatMessage>();
                Messages[sessionId] = sessionMessages;
            }

            sessionMessages.Add(message);
        }

        public List<ChatMessage> GetMessages(string sessionId)
            => Messages.TryGetValue(sessionId, out var sessionMessages)
                ? new List<ChatMessage>(sessionMessages)
                : new List<ChatMessage>();

        public void ClearSession(string sessionId)
        {
            Messages.Remove(sessionId);
        }

        public void UpdateMessage(string sessionId, string messageId, Action<ChatMessage> updateAction)
        {
            var message = GetMessage(sessionId, messageId);
            if (message != null)
            {
                updateAction(message);
            }
        }

        public ChatMessage? GetMessage(string sessionId, string messageId)
        {
            return null;
        }
    }

    private sealed class TakeoverCliExecutor(string workspacePath) : ICliExecutorService
    {
        private readonly Dictionary<string, string> _cliThreadIds = new(StringComparer.OrdinalIgnoreCase);
        private int _callCount;

        public TaskCompletionSource<bool> ThreadIdPersisted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<ExecutionCall> ExecuteCalls { get; } = new();

        public bool BlockFirstCall { get; set; } = true;

        public ICliToolAdapter? GetAdapter(CliToolConfig tool) => new CodexAdapter();

        public ICliToolAdapter? GetAdapterById(string toolId) => new CodexAdapter();

        public bool SupportsStreamParsing(CliToolConfig tool) => true;

        public string? GetCliThreadId(string sessionId)
        {
            return _cliThreadIds.TryGetValue(sessionId, out var threadId) ? threadId : null;
        }

        public void SetCliThreadId(string sessionId, string threadId)
        {
            _cliThreadIds[sessionId] = threadId;
            if (string.Equals(threadId, "thread-1", StringComparison.Ordinal))
            {
                ThreadIdPersisted.TrySetResult(true);
            }
        }

        public async IAsyncEnumerable<StreamOutputChunk> ExecuteStreamAsync(
            string sessionId,
            string toolId,
            string userPrompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var callNumber = Interlocked.Increment(ref _callCount);
            ExecuteCalls.Add(new ExecutionCall
            {
                SessionId = sessionId,
                ToolId = toolId,
                Prompt = userPrompt,
                ThreadIdAtStart = GetCliThreadId(sessionId)
            });

            if (callNumber == 1 && BlockFirstCall)
            {
                yield return new StreamOutputChunk
                {
                    Content = "{\"type\":\"thread.started\",\"thread_id\":\"thread-1\"}\n",
                    IsCompleted = false
                };

                var wasCancelled = false;
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    wasCancelled = true;
                }

                if (wasCancelled)
                {
                    yield return new StreamOutputChunk
                    {
                        IsError = true,
                        IsCompleted = true,
                        ErrorMessage = "执行已取消"
                    };
                    yield break;
                }
            }

            yield return new StreamOutputChunk
            {
                Content = "补充完成\n",
                IsCompleted = false
            };

            yield return new StreamOutputChunk
            {
                Content = string.Empty,
                IsCompleted = true
            };
        }

        public List<CliToolConfig> GetAvailableTools(string? username = null)
            => new() { GetTool("codex", username)! };

        public CliToolConfig? GetTool(string toolId, string? username = null)
            => string.Equals(toolId, "codex", StringComparison.OrdinalIgnoreCase)
                ? new CliToolConfig
                {
                    Id = "codex",
                    Name = "Codex",
                    Description = "Codex",
                    Command = "codex",
                    Enabled = true
                }
                : null;

        public bool ValidateTool(string toolId, string? username = null) => true;

        public void CleanupSessionWorkspace(string sessionId)
        {
        }

        public void CleanupExpiredWorkspaces()
        {
        }

        public string GetSessionWorkspacePath(string sessionId) => workspacePath;

        public Task<Dictionary<string, string>> GetToolEnvironmentVariablesAsync(string toolId, string? username = null)
            => Task.FromResult(new Dictionary<string, string>());

        public Task<bool> SaveToolEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars, string? username = null)
            => Task.FromResult(true);

        public byte[]? GetWorkspaceFile(string sessionId, string relativePath) => null;

        public byte[]? GetWorkspaceZip(string sessionId) => null;

        public Task<bool> UploadFileToWorkspaceAsync(string sessionId, string fileName, byte[] fileContent, string? relativePath = null)
            => Task.FromResult(true);

        public Task<bool> CreateFolderInWorkspaceAsync(string sessionId, string folderPath)
            => Task.FromResult(true);

        public Task<bool> DeleteWorkspaceItemAsync(string sessionId, string relativePath, bool isDirectory)
            => Task.FromResult(true);

        public Task<bool> MoveFileInWorkspaceAsync(string sessionId, string sourcePath, string targetPath)
            => Task.FromResult(true);

        public Task<bool> CopyFileInWorkspaceAsync(string sessionId, string sourcePath, string targetPath)
            => Task.FromResult(true);

        public Task<bool> RenameFileInWorkspaceAsync(string sessionId, string oldPath, string newName)
            => Task.FromResult(true);

        public Task<int> BatchDeleteFilesAsync(string sessionId, List<string> relativePaths)
            => Task.FromResult(0);

        public Task<string> InitializeSessionWorkspaceAsync(string sessionId, string? projectId = null, bool includeGit = false)
            => Task.FromResult(Path.Combine(Path.GetTempPath(), sessionId));

        public void RefreshWorkspaceRootCache()
        {
        }
    }

    private sealed class PromptCapturingCliExecutor(string workspacePath) : ICliExecutorService
    {
        public List<ExecutionCall> ExecuteCalls { get; } = new();

        public ICliToolAdapter? GetAdapter(CliToolConfig tool) => null;

        public ICliToolAdapter? GetAdapterById(string toolId) => null;

        public bool SupportsStreamParsing(CliToolConfig tool) => false;

        public string? GetCliThreadId(string sessionId) => null;

        public void SetCliThreadId(string sessionId, string threadId)
        {
        }

        public async IAsyncEnumerable<StreamOutputChunk> ExecuteStreamAsync(
            string sessionId,
            string toolId,
            string userPrompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ExecuteCalls.Add(new ExecutionCall
            {
                SessionId = sessionId,
                ToolId = toolId,
                Prompt = userPrompt
            });

            yield return new StreamOutputChunk
            {
                Content = "补充完成\n",
                IsCompleted = false
            };

            yield return new StreamOutputChunk
            {
                Content = string.Empty,
                IsCompleted = true
            };

            await Task.CompletedTask;
        }

        public List<CliToolConfig> GetAvailableTools(string? username = null)
            => new() { GetTool("codex", username)! };

        public CliToolConfig? GetTool(string toolId, string? username = null)
            => string.Equals(toolId, "codex", StringComparison.OrdinalIgnoreCase)
                ? new CliToolConfig
                {
                    Id = "codex",
                    Name = "Codex",
                    Description = "Codex",
                    Command = "codex",
                    Enabled = true
                }
                : null;

        public bool ValidateTool(string toolId, string? username = null) => true;

        public void CleanupSessionWorkspace(string sessionId)
        {
        }

        public void CleanupExpiredWorkspaces()
        {
        }

        public string GetSessionWorkspacePath(string sessionId) => workspacePath;

        public Task<Dictionary<string, string>> GetToolEnvironmentVariablesAsync(string toolId, string? username = null)
            => Task.FromResult(new Dictionary<string, string>());

        public Task<bool> SaveToolEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars, string? username = null)
            => Task.FromResult(true);

        public byte[]? GetWorkspaceFile(string sessionId, string relativePath) => null;

        public byte[]? GetWorkspaceZip(string sessionId) => null;

        public Task<bool> UploadFileToWorkspaceAsync(string sessionId, string fileName, byte[] fileContent, string? relativePath = null)
            => Task.FromResult(true);

        public Task<bool> CreateFolderInWorkspaceAsync(string sessionId, string folderPath)
            => Task.FromResult(true);

        public Task<bool> DeleteWorkspaceItemAsync(string sessionId, string relativePath, bool isDirectory)
            => Task.FromResult(true);

        public Task<bool> MoveFileInWorkspaceAsync(string sessionId, string sourcePath, string targetPath)
            => Task.FromResult(true);

        public Task<bool> CopyFileInWorkspaceAsync(string sessionId, string sourcePath, string targetPath)
            => Task.FromResult(true);

        public Task<bool> RenameFileInWorkspaceAsync(string sessionId, string oldPath, string newName)
            => Task.FromResult(true);

        public Task<int> BatchDeleteFilesAsync(string sessionId, List<string> relativePaths)
            => Task.FromResult(0);

        public Task<string> InitializeSessionWorkspaceAsync(string sessionId, string? projectId = null, bool includeGit = false)
            => Task.FromResult(workspacePath);

        public void RefreshWorkspaceRootCache()
        {
        }
    }

    private sealed class ExecutionCall
    {
        public string SessionId { get; set; } = string.Empty;

        public string ToolId { get; set; } = string.Empty;

        public string Prompt { get; set; } = string.Empty;

        public string? ThreadIdAtStart { get; set; }
    }

    private sealed class RecordingSessionDirectoryService(ChatSessionRepositoryProxy repository) : ISessionDirectoryService
    {
        public Task SetSessionWorkspaceAsync(string sessionId, string username, string directoryPath, bool isCustom = true)
        {
            var session = repository.GetStored(sessionId);
            session.WorkspacePath = directoryPath;
            session.IsCustomWorkspace = isCustom;
            session.UpdatedAt = DateTime.Now;
            repository.Store(session);
            return Task.CompletedTask;
        }

        public Task<string?> GetSessionWorkspaceAsync(string sessionId, string username)
            => Task.FromResult<string?>(repository.GetStored(sessionId).WorkspacePath);

        public Task SwitchSessionWorkspaceAsync(string sessionId, string username, string newDirectoryPath)
            => SetSessionWorkspaceAsync(sessionId, username, newDirectoryPath);

        public Task<bool> VerifySessionWorkspacePermissionAsync(string sessionId, string username, string requiredPermission = "write")
            => Task.FromResult(true);

        public Task<List<object>> GetUserAccessibleDirectoriesAsync(string username)
            => Task.FromResult(new List<object>());

        public Task<AllowedDirectoryBrowseResult> BrowseAllowedDirectoriesAsync(string? path, string? username = null)
            => throw new NotSupportedException();
    }

    private class ChatSessionRepositoryProxy : DispatchProxy
    {
        private readonly Dictionary<string, ChatSessionEntity> _sessions = new(StringComparer.OrdinalIgnoreCase);

        public ChatSessionEntity GetStored(string sessionId)
        {
            return Clone(_sessions[sessionId]);
        }

        public void Store(ChatSessionEntity session)
        {
            _sessions[session.SessionId] = Clone(session);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IChatSessionRepository.CreateFeishuSessionAsync):
                {
                    var chatKey = (string)args![0]!;
                    var username = (string)args[1]!;
                    var workspacePath = args[2] as string;
                    var toolId = args[3] as string;
                    var sessionId = Guid.NewGuid().ToString();

                    _sessions[sessionId] = new ChatSessionEntity
                    {
                        SessionId = sessionId,
                        Username = username,
                        FeishuChatKey = chatKey,
                        IsFeishuActive = true,
                        WorkspacePath = workspacePath,
                        ToolId = toolId,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    return Task.FromResult(sessionId);
                }
                case nameof(IRepository<ChatSessionEntity>.GetByIdAsync):
                {
                    var sessionId = args![0]?.ToString() ?? string.Empty;
                    var session = _sessions.TryGetValue(sessionId, out var stored) ? Clone(stored) : null;
                    return Task.FromResult(session);
                }
                case nameof(IRepository<ChatSessionEntity>.UpdateAsync):
                {
                    var session = Clone((ChatSessionEntity)args![0]!);
                    _sessions[session.SessionId] = session;
                    return Task.FromResult(true);
                }
                case nameof(IChatSessionRepository.GetByIdAndUsernameAsync):
                {
                    var sessionId = args![0]?.ToString() ?? string.Empty;
                    var username = args[1]?.ToString() ?? string.Empty;
                    var session = _sessions.TryGetValue(sessionId, out var stored) &&
                                  string.Equals(stored.Username, username, StringComparison.OrdinalIgnoreCase)
                        ? Clone(stored)
                        : null;
                    return Task.FromResult(session);
                }
                case nameof(IChatSessionRepository.GetByUsernameOrderByUpdatedAtAsync):
                {
                    var username = args![0]?.ToString() ?? string.Empty;
                    var sessions = _sessions.Values
                        .Where(session => string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(session => session.UpdatedAt)
                        .Select(Clone)
                        .ToList();
                    return Task.FromResult(sessions);
                }
                case nameof(IChatSessionRepository.GetByFeishuChatKeyAsync):
                {
                    var chatKey = args![0]?.ToString() ?? string.Empty;
                    var sessions = _sessions.Values
                        .Where(session => string.Equals(session.FeishuChatKey, chatKey, StringComparison.OrdinalIgnoreCase))
                        .Select(Clone)
                        .ToList();
                    return Task.FromResult(sessions);
                }
                case nameof(IRepository<ChatSessionEntity>.GetListAsync):
                {
                    if (args == null || args.Length == 0 || args[0] == null)
                    {
                        return Task.FromResult(_sessions.Values.Select(Clone).ToList());
                    }

                    var predicate = (Expression<Func<ChatSessionEntity, bool>>)args[0]!;
                    var compiled = predicate.Compile();
                    var sessions = _sessions.Values
                        .Where(compiled)
                        .Select(Clone)
                        .ToList();
                    return Task.FromResult(sessions);
                }
            }

            return GetDefaultReturnValue(targetMethod?.ReturnType);
        }

        private static object? GetDefaultReturnValue(Type? returnType)
        {
            if (returnType == null || returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GenericTypeArguments[0];
                var defaultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [defaultValue]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }

        private static ChatSessionEntity Clone(ChatSessionEntity session)
        {
            return new ChatSessionEntity
            {
                SessionId = session.SessionId,
                Username = session.Username,
                Title = session.Title,
                WorkspacePath = session.WorkspacePath,
                ToolId = session.ToolId,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt,
                IsWorkspaceValid = session.IsWorkspaceValid,
                ProjectId = session.ProjectId,
                FeishuChatKey = session.FeishuChatKey,
                IsFeishuActive = session.IsFeishuActive,
                IsCustomWorkspace = session.IsCustomWorkspace
            };
        }
    }

    private class DefaultInterfaceProxy<T> : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType ?? typeof(void);
            if (returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GenericTypeArguments[0];
                var defaultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [defaultValue]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
