using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Im.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public class FeishuCardActionServiceTests
{
    [Fact]
    public async Task HandleCardActionAsync_ExecuteCommand_UsesCurrentFeishuSession()
    {
        const string chatId = "oc_current_chat";
        const string activeSessionId = "session-acp";

        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(activeSessionId);
        var service = CreateService(cliExecutor, feishuChannel, new TestServiceProvider());

        await service.HandleCardActionAsync(
            """{"action":"execute_command"}""",
            chatId: chatId,
            inputValues: "/init");

        var usedSessionId = await cliExecutor.WaitForExecutionAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(activeSessionId, usedSessionId);
    }

    [Fact]
    public async Task HandleCardActionAsync_ExecuteCommand_WithoutActiveSession_ReturnsCreateSessionForm()
    {
        const string chatId = "oc_current_chat";

        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(null);
        var service = CreateService(cliExecutor, feishuChannel, new TestServiceProvider());

        var response = await service.HandleCardActionAsync(
            """{"action":"execute_command"}""",
            chatId: chatId,
            inputValues: "/init");

        Assert.Equal(CardActionTriggerResponseDto.ToastSuffix.ToastType.Warning, response.Toast?.Type);
        Assert.Contains("当前没有活跃会话", response.Toast?.Content);
        Assert.False(cliExecutor.WasExecuted);
    }

    private static FeishuCardActionService CreateService(
        RecordingCliExecutorService cliExecutor,
        StubFeishuChannelService feishuChannel,
        IServiceProvider serviceProvider)
    {
        var commandService = new FeishuCommandService(
            NullLogger<FeishuCommandService>.Instance,
            new CommandScannerService());

        return new FeishuCardActionService(
            commandService,
            new FeishuHelpCardBuilder(),
            new StubFeishuCardKitClient(),
            cliExecutor,
            new StubChatSessionService(),
            feishuChannel,
            NullLogger<FeishuCardActionService>.Instance,
            serviceProvider);
    }

    private sealed class RecordingCliExecutorService : ICliExecutorService
    {
        private readonly TaskCompletionSource<string> _executionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CliToolConfig _tool = new() { Id = "claude-code", Name = "Claude Code" };

        public bool WasExecuted { get; private set; }

        public async Task<string> WaitForExecutionAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            using var _ = cts.Token.Register(() => _executionStarted.TrySetCanceled(cts.Token));
            return await _executionStarted.Task;
        }

        public ICliToolAdapter? GetAdapter(CliToolConfig tool) => null;

        public ICliToolAdapter? GetAdapterById(string toolId) => null;

        public bool SupportsStreamParsing(CliToolConfig tool) => false;

        public string? GetCliThreadId(string sessionId) => null;

        public void SetCliThreadId(string sessionId, string threadId) { }

        public async IAsyncEnumerable<StreamOutputChunk> ExecuteStreamAsync(
            string sessionId,
            string toolId,
            string userPrompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            WasExecuted = true;
            _executionStarted.TrySetResult(sessionId);
            yield return new StreamOutputChunk
            {
                Content = "initialized",
                IsCompleted = true
            };
            await Task.CompletedTask;
        }

        public List<CliToolConfig> GetAvailableTools() => [_tool];

        public CliToolConfig? GetTool(string toolId) => toolId == _tool.Id ? _tool : null;

        public bool ValidateTool(string toolId) => toolId == _tool.Id;

        public void CleanupSessionWorkspace(string sessionId) { }

        public void CleanupExpiredWorkspaces() { }

        public string GetSessionWorkspacePath(string sessionId) => sessionId;

        public Task<Dictionary<string, string>> GetToolEnvironmentVariablesAsync(string toolId) => Task.FromResult(new Dictionary<string, string>());

        public Task<bool> SaveToolEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars) => Task.FromResult(true);

        public byte[]? GetWorkspaceFile(string sessionId, string relativePath) => null;

        public byte[]? GetWorkspaceZip(string sessionId) => null;

        public Task<bool> UploadFileToWorkspaceAsync(string sessionId, string fileName, byte[] fileContent, string? relativePath = null) => Task.FromResult(true);

        public Task<bool> CreateFolderInWorkspaceAsync(string sessionId, string folderPath) => Task.FromResult(true);

        public Task<bool> DeleteWorkspaceItemAsync(string sessionId, string relativePath, bool isDirectory) => Task.FromResult(true);

        public Task<bool> MoveFileInWorkspaceAsync(string sessionId, string sourcePath, string targetPath) => Task.FromResult(true);

        public Task<bool> CopyFileInWorkspaceAsync(string sessionId, string sourcePath, string targetPath) => Task.FromResult(true);

        public Task<bool> RenameFileInWorkspaceAsync(string sessionId, string oldPath, string newName) => Task.FromResult(true);

        public Task<int> BatchDeleteFilesAsync(string sessionId, List<string> relativePaths) => Task.FromResult(0);

        public Task<string> InitializeSessionWorkspaceAsync(string sessionId, string? projectId = null, bool includeGit = false) => Task.FromResult(sessionId);

        public void RefreshWorkspaceRootCache() { }
    }

    private sealed class StubChatSessionService : IChatSessionService
    {
        public void AddMessage(string sessionId, ChatMessage message) { }

        public void ClearSession(string sessionId) { }

        public ChatMessage? GetMessage(string sessionId, string messageId) => null;

        public List<ChatMessage> GetMessages(string sessionId) => [];

        public void UpdateMessage(string sessionId, string messageId, Action<ChatMessage> updateAction) { }
    }

    private sealed class StubFeishuCardKitClient : IFeishuCardKitClient
    {
        public Task<string> CreateCardAsync(string initialContent, string? title = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdateCardAsync(string cardId, string content, int sequence, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<string> SendCardMessageAsync(string chatId, string cardId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> ReplyCardMessageAsync(string replyMessageId, string cardId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FeishuStreamingHandle> CreateStreamingHandleAsync(string chatId, string? replyMessageId, string initialContent, string? title = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FeishuStreamingHandle(
                "card-1",
                "message-1",
                _ => Task.CompletedTask,
                _ => Task.CompletedTask,
                throttleMs: 0));
        }

        public Task<string> SendRawCardAsync(string chatId, string cardJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> ReplyElementsCardAsync(string replyMessageId, ElementsCardV2Dto card, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> ReplyRawCardAsync(string replyMessageId, string cardJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubFeishuChannelService(string? currentSessionId) : IFeishuChannelService
    {
        public bool IsRunning => true;

        public Task<string> SendMessageAsync(string chatId, string content) => Task.FromResult("notify-message");

        public Task<string> ReplyMessageAsync(string messageId, string content) => Task.FromResult("reply-message");

        public Task<FeishuStreamingHandle> SendStreamingMessageAsync(string chatId, string initialContent, string? replyToMessageId = null) => throw new NotSupportedException();

        public Task HandleIncomingMessageAsync(FeishuIncomingMessage message) => throw new NotSupportedException();

        public string? GetCurrentSession(string chatKey, string? username = null) => currentSessionId;

        public DateTime? GetSessionLastActiveTime(string sessionId) => DateTime.UtcNow;

        public List<string> GetChatSessions(string chatKey, string? username = null) => currentSessionId == null ? [] : [currentSessionId];

        public bool SwitchCurrentSession(string chatKey, string sessionId, string? username = null) => true;

        public bool CloseSession(string chatKey, string sessionId, string? username = null) => true;

        public string CreateNewSession(FeishuIncomingMessage message, string? customWorkspacePath = null, string? toolId = null) => throw new NotSupportedException();

        public string? GetSessionUsername(string chatKey) => "luhaiyan";

        public string ResolveToolId(string chatKey, string? username = null) => "claude-code";
    }

    private sealed class TestServiceProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        private readonly StubFeishuUserBindingService _bindingService = new();
        private readonly StubSessionDirectoryService _sessionDirectoryService = new();

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            if (serviceType == typeof(IFeishuUserBindingService))
            {
                return _bindingService;
            }

            if (serviceType == typeof(ISessionDirectoryService))
            {
                return _sessionDirectoryService;
            }

            return null;
        }

        public IServiceScope CreateScope() => this;

        public IServiceProvider ServiceProvider => this;

        public void Dispose()
        {
        }
    }

    private sealed class StubFeishuUserBindingService : IFeishuUserBindingService
    {
        public Task<string?> GetBoundWebUsernameAsync(string feishuUserId) => Task.FromResult<string?>(null);

        public Task<bool> IsBoundAsync(string feishuUserId) => Task.FromResult(false);

        public Task<(bool Success, string? ErrorMessage, string? WebUsername)> BindAsync(string feishuUserId, string webUsername)
            => Task.FromResult((true, (string?)null, (string?)webUsername));

        public Task<bool> UnbindAsync(string feishuUserId) => Task.FromResult(true);

        public Task<List<string>> GetBindableWebUsernamesAsync() => Task.FromResult(new List<string>());

        public Task<HashSet<string>> GetAllBoundWebUsernamesAsync() => Task.FromResult(new HashSet<string>());
    }

    private sealed class StubSessionDirectoryService : ISessionDirectoryService
    {
        public Task SetSessionWorkspaceAsync(string sessionId, string username, string directoryPath, bool isCustom = true) => Task.CompletedTask;

        public Task<string?> GetSessionWorkspaceAsync(string sessionId, string username) => Task.FromResult<string?>(null);

        public Task SwitchSessionWorkspaceAsync(string sessionId, string username, string newDirectoryPath) => Task.CompletedTask;

        public Task<bool> VerifySessionWorkspacePermissionAsync(string sessionId, string username, string requiredPermission = "write") => Task.FromResult(true);

        public Task<List<object>> GetUserAccessibleDirectoriesAsync(string username)
        {
            var directories = new List<object>
            {
                new
                {
                    Type = "owned",
                    DirectoryType = "workspace",
                    Alias = "ACP",
                    Permission = "owner",
                    DirectoryPath = @"D:\VSWorkshop\acp"
                }
            };

            return Task.FromResult(directories);
        }
    }
}
