using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Im.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
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

    [Fact]
    public async Task HandleCardActionAsync_BrowseCurrentSessionDirectory_ReturnsDirectoryCard()
    {
        const string chatId = "oc_workspace_chat";
        const string activeSessionId = "session-files";

        var workspacePath = Path.Combine(Path.GetTempPath(), $"feishu-browse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);
        Directory.CreateDirectory(Path.Combine(workspacePath, "src"));
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "README.md"), "hello from feishu");

        try
        {
            var cliExecutor = new RecordingCliExecutorService();
            cliExecutor.SetSessionWorkspacePath(activeSessionId, workspacePath);

            var feishuChannel = new StubFeishuChannelService(activeSessionId);
            var service = CreateService(cliExecutor, feishuChannel, new TestServiceProvider());

            var response = await service.HandleCardActionAsync(
                """{"action":"browse_current_session_directory","chat_key":"oc_workspace_chat"}""",
                chatId: chatId);

            var payload = SerializeResponse(response);
            Assert.Contains("README.md", payload);
            Assert.Contains("src", payload);
            Assert.Contains("\"action\":\"preview_session_file\"", payload);
            Assert.Contains("\"action\":\"browse_session_directory\"", payload);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task HandleCardActionAsync_PreviewSessionFile_ReturnsTextPreview()
    {
        const string chatId = "oc_workspace_chat";
        const string activeSessionId = "session-files";

        var workspacePath = Path.Combine(Path.GetTempPath(), $"feishu-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "README.md"), "hello from feishu\nsecond line");

        try
        {
            var cliExecutor = new RecordingCliExecutorService();
            cliExecutor.SetSessionWorkspacePath(activeSessionId, workspacePath);

            var feishuChannel = new StubFeishuChannelService(activeSessionId);
            var service = CreateService(cliExecutor, feishuChannel, new TestServiceProvider());

            var response = await service.HandleCardActionAsync(
                """{"action":"preview_session_file","chat_key":"oc_workspace_chat","session_id":"session-files","file_path":"README.md","directory_path":"","page":0}""",
                chatId: chatId);

            var payload = SerializeResponse(response);
            Assert.Contains("hello from feishu", payload);
            Assert.Contains("second line", payload);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task HandleCardActionAsync_OpenProjectManager_ReturnsProjectListForBoundUser()
    {
        const string chatId = "oc_project_chat";
        var userContext = new TestUserContextService();
        var projectService = new TestProjectService(userContext, [
            new ProjectInfo
            {
                ProjectId = "project-1",
                Name = "WmsServerV4",
                GitUrl = "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
                AuthType = "https",
                Branch = string.Empty,
                Status = "ready",
                LocalPath = @"D:\repos\WmsServerV4",
                UpdatedAt = DateTime.Now
            }
        ]);

        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(null);
        var serviceProvider = new TestServiceProvider(userContext, projectService);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider);

        var response = await service.HandleCardActionAsync(
            """{"action":"open_project_manager","chat_key":"oc_project_chat"}""",
            chatId: chatId);

        var payload = SerializeResponse(response);
        Assert.Contains("WmsServerV4", payload);
        Assert.Contains("create_session_from_project", payload);
        Assert.Equal("luhaiyan", projectService.LastUsernameSeen);
    }

    [Fact]
    public async Task HandleCardActionAsync_ShowCreateProjectForm_IncludesNamedSubmitButtons()
    {
        const string chatId = "oc_project_chat";
        var userContext = new TestUserContextService();
        var projectService = new TestProjectService(userContext);
        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(null);
        var serviceProvider = new TestServiceProvider(userContext, projectService);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider);

        var response = await service.HandleCardActionAsync(
            """{"action":"show_create_project_form","chat_key":"oc_project_chat"}""",
            chatId: chatId);

        var payload = SerializeResponse(response);
        Assert.Contains("\"name\":\"create_project_submit\"", payload);
        Assert.Contains("\"name\":\"fetch_project_branches_submit\"", payload);
    }

    [Fact]
    public async Task HandleCardActionAsync_ShowEditProjectForm_IncludesProjectScopedSubmitButtons()
    {
        const string chatId = "oc_project_chat";
        var userContext = new TestUserContextService();
        var projectService = new TestProjectService(userContext, [
            new ProjectInfo
            {
                ProjectId = "project-1",
                Name = "WmsServerV4",
                GitUrl = "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
                AuthType = "https",
                Branch = string.Empty,
                Status = "ready",
                LocalPath = @"D:\repos\WmsServerV4",
                UpdatedAt = DateTime.Now
            }
        ]);
        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(null);
        var serviceProvider = new TestServiceProvider(userContext, projectService);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider);

        var response = await service.HandleCardActionAsync(
            """{"action":"show_edit_project_form","chat_key":"oc_project_chat","project_id":"project-1"}""",
            chatId: chatId);

        var payload = SerializeResponse(response);
        Assert.Contains("\"name\":\"update_project_submit__project-1\"", payload);
        Assert.Contains("\"name\":\"fetch_project_branches_submit__project-1\"", payload);
    }

    [Fact]
    public async Task HandleCardActionAsync_CreateProject_UsesBoundWebUserContext()
    {
        const string chatId = "oc_project_chat";
        var userContext = new TestUserContextService();
        var projectService = new TestProjectService(userContext);
        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(null);
        var serviceProvider = new TestServiceProvider(userContext, projectService);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider);

        var response = await service.HandleCardActionAsync(
            """{"action":"create_project","chat_key":"oc_project_chat"}""",
            formValue: new Dictionary<string, object>
            {
                ["project_name"] = "TfsProject",
                ["project_git_url"] = "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
                ["project_auth_type"] = "https",
                ["project_https_username"] = "alice",
                ["project_https_token"] = "secret",
                ["project_branch"] = string.Empty
            },
            chatId: chatId);

        Assert.Equal("luhaiyan", projectService.LastUsernameSeen);
        Assert.NotNull(projectService.LastCreatedRequest);
        Assert.Equal(string.Empty, projectService.LastCreatedRequest!.Branch);
        Assert.Equal("alice", projectService.LastCreatedRequest.HttpsUsername);
        Assert.Equal("https", projectService.LastCreatedRequest.AuthType);

        var payload = SerializeResponse(response);
        Assert.Contains("TfsProject", payload);
    }

    [Fact]
    public async Task HandleCardActionAsync_CreateSessionFromProject_UsesProjectWorkspace()
    {
        const string chatId = "oc_project_chat";
        var projectPath = Path.Combine(Path.GetTempPath(), $"feishu-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectPath);

        try
        {
            var userContext = new TestUserContextService();
            var projectService = new TestProjectService(userContext, [
                new ProjectInfo
                {
                    ProjectId = "project-ready",
                    Name = "ReadyProject",
                    GitUrl = "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
                    AuthType = "https",
                    Branch = string.Empty,
                    Status = "ready",
                    LocalPath = projectPath,
                    UpdatedAt = DateTime.Now
                }
            ]);

            var cliExecutor = new RecordingCliExecutorService();
            var feishuChannel = new StubFeishuChannelService(null);
            var serviceProvider = new TestServiceProvider(userContext, projectService);
            var service = CreateService(cliExecutor, feishuChannel, serviceProvider);

            var response = await service.HandleCardActionAsync(
                """{"action":"create_session_from_project","chat_key":"oc_project_chat","project_id":"project-ready"}""",
                chatId: chatId);

            Assert.Equal(projectPath, feishuChannel.CreatedWorkspacePath);
            Assert.Equal("session-new", feishuChannel.LastSwitchedSessionId);
            Assert.Equal("claude-code", feishuChannel.CreatedToolId);

            var payload = SerializeResponse(response);
            Assert.Contains("ReadyProject", payload);
        }
        finally
        {
            Directory.Delete(projectPath, recursive: true);
        }
    }

    private static string SerializeResponse(CardActionTriggerResponseDto response)
    {
        return JsonSerializer.Serialize(response);
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
        private readonly Dictionary<string, string> _workspacePaths = new(StringComparer.OrdinalIgnoreCase);

        public bool WasExecuted { get; private set; }

        public void SetSessionWorkspacePath(string sessionId, string workspacePath)
        {
            _workspacePaths[sessionId] = workspacePath;
        }

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

        public string GetSessionWorkspacePath(string sessionId)
        {
            return _workspacePaths.TryGetValue(sessionId, out var workspacePath)
                ? workspacePath
                : sessionId;
        }

        public Task<Dictionary<string, string>> GetToolEnvironmentVariablesAsync(string toolId) => Task.FromResult(new Dictionary<string, string>());

        public Task<bool> SaveToolEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars) => Task.FromResult(true);

        public byte[]? GetWorkspaceFile(string sessionId, string relativePath)
        {
            var workspacePath = GetSessionWorkspacePath(sessionId);
            var fullPath = Path.Combine(workspacePath, relativePath);
            return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
        }

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

        public string? CreatedWorkspacePath { get; private set; }

        public string? CreatedToolId { get; private set; }

        public string? LastSwitchedSessionId { get; private set; }

        public Task<string> SendMessageAsync(string chatId, string content) => Task.FromResult("notify-message");

        public Task<string> ReplyMessageAsync(string messageId, string content) => Task.FromResult("reply-message");

        public Task<FeishuStreamingHandle> SendStreamingMessageAsync(string chatId, string initialContent, string? replyToMessageId = null) => throw new NotSupportedException();

        public Task HandleIncomingMessageAsync(FeishuIncomingMessage message) => throw new NotSupportedException();

        public string? GetCurrentSession(string chatKey, string? username = null) => currentSessionId;

        public DateTime? GetSessionLastActiveTime(string sessionId) => DateTime.UtcNow;

        public List<string> GetChatSessions(string chatKey, string? username = null) => currentSessionId == null ? [] : [currentSessionId];

        public bool SwitchCurrentSession(string chatKey, string sessionId, string? username = null)
        {
            LastSwitchedSessionId = sessionId;
            return true;
        }

        public bool CloseSession(string chatKey, string sessionId, string? username = null) => true;

        public string CreateNewSession(FeishuIncomingMessage message, string? customWorkspacePath = null, string? toolId = null)
        {
            CreatedWorkspacePath = customWorkspacePath;
            CreatedToolId = toolId;
            return "session-new";
        }

        public string? GetSessionUsername(string chatKey) => "luhaiyan";

        public string ResolveToolId(string chatKey, string? username = null) => "claude-code";
    }

    private sealed class TestServiceProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        private readonly StubFeishuUserBindingService _bindingService = new();
        private readonly StubSessionDirectoryService _sessionDirectoryService = new();
        private readonly TestUserContextService _userContextService;
        private readonly TestProjectService _projectService;

        public TestServiceProvider(TestUserContextService? userContextService = null, TestProjectService? projectService = null)
        {
            _userContextService = userContextService ?? new TestUserContextService();
            _projectService = projectService ?? new TestProjectService(_userContextService);
        }

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

            if (serviceType == typeof(IUserContextService))
            {
                return _userContextService;
            }

            if (serviceType == typeof(IProjectService))
            {
                return _projectService;
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

    private sealed class TestUserContextService : IUserContextService
    {
        private string _currentUsername = "default";

        public string GetCurrentUsername() => _currentUsername;

        public void SetCurrentUsername(string username)
        {
            _currentUsername = username;
        }
    }

    private sealed class TestProjectService : IProjectService
    {
        private readonly IUserContextService _userContextService;
        private readonly Dictionary<string, ProjectInfo> _projects;

        public TestProjectService(IUserContextService userContextService, IEnumerable<ProjectInfo>? seedProjects = null)
        {
            _userContextService = userContextService;
            _projects = (seedProjects ?? [])
                .ToDictionary(project => project.ProjectId, project => project, StringComparer.OrdinalIgnoreCase);
        }

        public string? LastUsernameSeen { get; private set; }

        public CreateProjectRequest? LastCreatedRequest { get; private set; }

        public Task<List<ProjectInfo>> GetProjectsAsync()
        {
            LastUsernameSeen = _userContextService.GetCurrentUsername();
            return Task.FromResult(_projects.Values.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase).ToList());
        }

        public Task<ProjectInfo?> GetProjectAsync(string projectId)
        {
            LastUsernameSeen = _userContextService.GetCurrentUsername();
            _projects.TryGetValue(projectId, out var project);
            return Task.FromResult(project);
        }

        public Task<(ProjectInfo? Project, string? ErrorMessage)> CreateProjectAsync(CreateProjectRequest request)
        {
            LastUsernameSeen = _userContextService.GetCurrentUsername();
            LastCreatedRequest = request;

            var project = new ProjectInfo
            {
                ProjectId = $"project-{_projects.Count + 1}",
                Name = request.Name,
                GitUrl = request.GitUrl,
                AuthType = request.AuthType,
                Branch = request.Branch,
                Status = "pending",
                UpdatedAt = DateTime.Now
            };

            _projects[project.ProjectId] = project;
            return Task.FromResult<(ProjectInfo?, string?)>((project, null));
        }

        public Task<(ProjectInfo? Project, string? ErrorMessage)> CreateProjectFromZipAsync(string projectName, byte[] zipFileContent)
            => throw new NotSupportedException();

        public Task<(bool Success, string? ErrorMessage)> UpdateProjectAsync(string projectId, UpdateProjectRequest request)
        {
            LastUsernameSeen = _userContextService.GetCurrentUsername();
            if (!_projects.TryGetValue(projectId, out var project))
            {
                return Task.FromResult((false, (string?)"项目不存在"));
            }

            project.Name = request.Name ?? project.Name;
            project.GitUrl = request.GitUrl ?? project.GitUrl;
            project.AuthType = request.AuthType ?? project.AuthType;
            project.Branch = request.Branch ?? project.Branch;
            project.UpdatedAt = DateTime.Now;
            return Task.FromResult((true, (string?)null));
        }

        public Task<(bool Success, string? ErrorMessage)> DeleteProjectAsync(string projectId)
        {
            LastUsernameSeen = _userContextService.GetCurrentUsername();
            return Task.FromResult((_projects.Remove(projectId), (string?)null));
        }

        public Task<(bool Success, string? ErrorMessage)> CloneProjectAsync(string projectId, Action<CloneProgress>? progress = null)
            => Task.FromResult((true, (string?)null));

        public Task<(bool Success, string? ErrorMessage)> PullProjectAsync(string projectId)
            => Task.FromResult((true, (string?)null));

        public Task<(List<string> Branches, string? ErrorMessage)> GetBranchesAsync(GetBranchesRequest request)
            => Task.FromResult<(List<string>, string?)>((["main", "release"], null));

        public string? GetProjectLocalPath(string projectId)
        {
            return _projects.TryGetValue(projectId, out var project) ? project.LocalPath : null;
        }

        public Task<(bool Success, string? ErrorMessage)> CopyProjectToWorkspaceAsync(string projectId, string targetPath, bool includeGit)
            => Task.FromResult((true, (string?)null));
    }
}
