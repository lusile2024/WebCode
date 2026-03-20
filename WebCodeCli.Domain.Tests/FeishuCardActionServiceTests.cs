using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Im.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using System.Linq.Expressions;
using System.Text.Json;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;
using WebCodeCli.Domain.Domain.Service.Channels;
using WebCodeCli.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.ChatSession;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

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
    public async Task HandleCardActionAsync_BrowseCurrentSessionDirectory_SkipsReservedWindowsDeviceEntries()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string chatId = "oc_workspace_chat";
        const string activeSessionId = "session-files";
        var workspacePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        var cliExecutor = new RecordingCliExecutorService();
        cliExecutor.SetSessionWorkspacePath(activeSessionId, workspacePath);

        var feishuChannel = new StubFeishuChannelService(activeSessionId);
        var service = CreateService(cliExecutor, feishuChannel, new TestServiceProvider());

        var response = await service.HandleCardActionAsync(
            """{"action":"browse_current_session_directory","chat_key":"oc_workspace_chat"}""",
            chatId: chatId);

        var payload = SerializeResponse(response);
        Assert.Contains("\"action\":\"browse_session_directory\"", payload);
        Assert.DoesNotContain("\"nul\"", payload, StringComparison.OrdinalIgnoreCase);
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
    public async Task HandleCardActionAsync_BrowseAllowedDirectory_ReturnsWhitelistBrowserCard()
    {
        const string chatId = "oc_allowed_chat";

        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(null);
        var service = CreateService(cliExecutor, feishuChannel, new TestServiceProvider());

        var response = await service.HandleCardActionAsync(
            """{"action":"browse_allowed_directory","chat_key":"oc_allowed_chat","tool_id":"claude-code"}""",
            chatId: chatId);

        var payload = SerializeResponse(response);
        Assert.Contains(@"D:\\VSWorkshop\\allowed", payload);
        Assert.Contains("\"action\":\"copy_path_to_chat\"", payload);
        Assert.Contains("\"action\":\"browse_allowed_directory\"", payload);
    }

    [Fact]
    public async Task HandleCardActionAsync_CreateSession_WithExistingWorkspace_DoesNotRequireCliWorkspaceCache()
    {
        const string chatId = "oc_workspace_chat";
        const string workspacePath = @"D:\VSWorkshop\TestWebCode\projects\luhaiyan\43cc07a314174cae9deb8cc2e69becaa";

        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(null);
        var service = CreateService(cliExecutor, feishuChannel, new TestServiceProvider());

        var response = await service.HandleCardActionAsync(
            """{"action":"create_session","chat_key":"oc_workspace_chat","create_mode":"existing","tool_id":"codex","workspace_path":"D:\\VSWorkshop\\TestWebCode\\projects\\luhaiyan\\43cc07a314174cae9deb8cc2e69becaa"}""",
            chatId: chatId,
            operatorUserId: "ou_test_user");

        Assert.Equal(CardActionTriggerResponseDto.ToastSuffix.ToastType.Success, response.Toast?.Type);
        Assert.Contains(workspacePath, response.Toast?.Content);
        Assert.Equal("session-new", feishuChannel.LastSwitchedSessionId);
        Assert.Equal(workspacePath, feishuChannel.CreatedWorkspacePath);
        Assert.Equal("codex", feishuChannel.CreatedToolId);
    }

    [Fact]
    public async Task HandleCardActionAsync_OpenSessionManager_FallsBackToSessionRepositoryWorkspace()
    {
        const string chatId = "oc_workspace_chat";
        const string activeSessionId = "session-db-backed";

        var workspacePath = Path.Combine(Path.GetTempPath(), $"feishu-session-manager-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);

        try
        {
            var cliExecutor = new RecordingCliExecutorService();
            var feishuChannel = new StubFeishuChannelService(activeSessionId);
            var sessionRepository = new StubChatSessionRepository(
            [
                new ChatSessionEntity
                {
                    SessionId = activeSessionId,
                    Username = "luhaiyan",
                    WorkspacePath = workspacePath,
                    ToolId = "codex",
                    FeishuChatKey = chatId,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsWorkspaceValid = true,
                    IsFeishuActive = true,
                    IsCustomWorkspace = true
                }
            ]);

            var serviceProvider = new TestServiceProvider(chatSessionRepository: sessionRepository);
            var service = CreateService(cliExecutor, feishuChannel, serviceProvider);

            var response = await service.HandleCardActionAsync(
                """{"action":"open_session_manager","chat_key":"oc_workspace_chat"}""",
                chatId: chatId);

            var payload = SerializeResponse(response);
            Assert.Contains(workspacePath.Replace(@"\", @"\\"), payload);
            Assert.DoesNotContain("工作区未初始化或已失效", payload);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task HandleCardActionAsync_CloseSession_WithMissingWorkspace_ClosesImmediately()
    {
        const string chatId = "oc_workspace_chat";
        const string sessionId = "session-missing-workspace";

        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(sessionId);
        var sessionRepository = new StubChatSessionRepository(
        [
            new ChatSessionEntity
            {
                SessionId = sessionId,
                Username = "luhaiyan",
                WorkspacePath = null,
                ToolId = "codex",
                FeishuChatKey = chatId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                IsWorkspaceValid = true,
                IsFeishuActive = true,
                IsCustomWorkspace = false
            }
        ]);

        var serviceProvider = new TestServiceProvider(chatSessionRepository: sessionRepository);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider);

        var response = await service.HandleCardActionAsync(
            """{"action":"close_session","chat_key":"oc_workspace_chat","session_id":"session-missing-workspace"}""",
            chatId: chatId,
            operatorUserId: "ou_test_user");

        Assert.Equal(CardActionTriggerResponseDto.ToastSuffix.ToastType.Info, response.Toast?.Type);
        Assert.Equal(sessionId, feishuChannel.LastClosedSessionId);
        Assert.Contains("已关闭会话", response.Toast?.Content);
    }

    [Fact]
    public async Task HandleCardActionAsync_CopyPathToChat_SendsCopyableMessage()
    {
        const string chatId = "oc_allowed_chat";
        const string path = @"D:\VSWorkshop\allowed\README.md";

        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(null);
        var service = CreateService(cliExecutor, feishuChannel, new TestServiceProvider());
        var actionJson = JsonSerializer.Serialize(new
        {
            action = "copy_path_to_chat",
            chat_key = chatId,
            copy_path = path
        });

        var response = await service.HandleCardActionAsync(
            actionJson,
            chatId: chatId);

        Assert.Equal(CardActionTriggerResponseDto.ToastSuffix.ToastType.Success, response.Toast?.Type);
        Assert.Equal("oc_allowed_chat", feishuChannel.LastSentChatId);
        Assert.Contains(path, feishuChannel.LastSentMessage);
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
        Assert.Contains("show_project_branch_switcher", payload);
        Assert.Equal("luhaiyan", projectService.LastUsernameSeen);
    }

    [Fact]
    public async Task HandleCardActionAsync_ShowProjectBranchSwitcher_ReturnsImmediateToastAndSendsBranchCardAsync()
    {
        const string chatId = "oc_project_chat";
        const string appId = "cli_user_branch";
        var userContext = new TestUserContextService();
        var projectService = new TestProjectService(userContext, [
            new ProjectInfo
            {
                ProjectId = "project-1",
                Name = "WmsServerV4",
                GitUrl = "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
                AuthType = "https",
                Branch = "main",
                Status = "ready",
                LocalPath = @"D:\repos\WmsServerV4",
                UpdatedAt = DateTime.Now
            }
        ]);

        var cliExecutor = new RecordingCliExecutorService();
        var cardKit = new StubFeishuCardKitClient();
        var feishuChannel = new StubFeishuChannelService(null);
        var serviceProvider = new TestServiceProvider(userContext, projectService);
        projectService.GetProjectBranchesDelay = TimeSpan.FromSeconds(2);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider, cardKit);

        var startedAt = DateTime.UtcNow;
        var response = await service.HandleCardActionAsync(
            """{"action":"show_project_branch_switcher","chat_key":"oc_project_chat","project_id":"project-1"}""",
            chatId: chatId,
            appId: appId);
        var elapsed = DateTime.UtcNow - startedAt;

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Expected immediate callback response, actual: {elapsed}");
        Assert.Equal(CardActionTriggerResponseDto.ToastSuffix.ToastType.Info, response.Toast?.Type);

        var (_, cardJson) = await cardKit.WaitForRawCardSentAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("switch_project_branch", cardJson);
        Assert.Contains("release", cardJson);
        Assert.Contains("main", cardJson);
        Assert.Equal(appId, cardKit.LastRawCardOptionsOverride?.AppId);
    }

    [Fact]
    public async Task HandleCardActionAsync_CloneProject_UsesBoundBotContextForCompletionNotification()
    {
        const string chatId = "oc_project_chat";
        const string appId = "cli_user_clone";
        var userContext = new TestUserContextService();
        var projectService = new TestProjectService(userContext, [
            new ProjectInfo
            {
                ProjectId = "project-1",
                Name = "WmsServerV4",
                GitUrl = "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
                AuthType = "https",
                Branch = string.Empty,
                Status = "pending",
                LocalPath = @"D:\repos\WmsServerV4",
                UpdatedAt = DateTime.Now
            }
        ])
        {
            CloneProjectDelay = TimeSpan.FromMilliseconds(50)
        };

        var cliExecutor = new RecordingCliExecutorService();
        var feishuChannel = new StubFeishuChannelService(null);
        var serviceProvider = new TestServiceProvider(userContext, projectService);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider);

        var response = await service.HandleCardActionAsync(
            """{"action":"clone_project","chat_key":"oc_project_chat","project_id":"project-1"}""",
            chatId: chatId,
            operatorUserId: "ou_test_user",
            appId: appId);

        Assert.Equal(CardActionTriggerResponseDto.ToastSuffix.ToastType.Info, response.Toast?.Type);

        var message = await feishuChannel.WaitForMessageAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(chatId, message.ChatId);
        Assert.Contains("项目 WmsServerV4 克隆完成", message.Content);
        Assert.Equal("luhaiyan", message.Username);
        Assert.Equal(appId, message.AppId);
    }

    [Fact]
    public async Task HandleCardActionAsync_ShowProjectBranchSwitcher_PaginatesLargeBranchListAsync()
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
                Branch = "branch-01",
                Status = "ready",
                LocalPath = @"D:\repos\WmsServerV4",
                UpdatedAt = DateTime.Now
            }
        ])
        {
            ProjectBranches = Enumerable.Range(1, 25)
                .Select(index => $"branch-{index:00}")
                .ToList()
        };

        var cliExecutor = new RecordingCliExecutorService();
        var cardKit = new StubFeishuCardKitClient();
        var feishuChannel = new StubFeishuChannelService(null);
        var serviceProvider = new TestServiceProvider(userContext, projectService);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider, cardKit);

        var response = await service.HandleCardActionAsync(
            """{"action":"show_project_branch_switcher","chat_key":"oc_project_chat","project_id":"project-1"}""",
            chatId: chatId);

        Assert.Equal(CardActionTriggerResponseDto.ToastSuffix.ToastType.Info, response.Toast?.Type);

        var (_, cardJson) = await cardKit.WaitForRawCardSentAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("branch-01", cardJson);
        Assert.Contains("branch-12", cardJson);
        Assert.DoesNotContain("branch-13", cardJson);
        Assert.Contains("1/3", cardJson);
        Assert.Contains("\"action\":\"show_project_branch_switcher\"", cardJson);
        Assert.Contains("\"page\":1", cardJson);
    }

    [Fact]
    public async Task HandleCardActionAsync_SwitchProjectBranch_ReturnsImmediateToastAndSendsUpdatedManagerCardAsync()
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
                Branch = "main",
                Status = "ready",
                LocalPath = @"D:\repos\WmsServerV4",
                UpdatedAt = DateTime.Now
            }
        ]);

        var cliExecutor = new RecordingCliExecutorService();
        var cardKit = new StubFeishuCardKitClient();
        var feishuChannel = new StubFeishuChannelService(null);
        var serviceProvider = new TestServiceProvider(userContext, projectService);
        projectService.SwitchProjectBranchDelay = TimeSpan.FromSeconds(2);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider, cardKit);

        var startedAt = DateTime.UtcNow;
        var response = await service.HandleCardActionAsync(
            """{"action":"switch_project_branch","chat_key":"oc_project_chat","project_id":"project-1","branch":"release"}""",
            chatId: chatId);
        var elapsed = DateTime.UtcNow - startedAt;

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Expected immediate callback response, actual: {elapsed}");
        Assert.Equal(CardActionTriggerResponseDto.ToastSuffix.ToastType.Info, response.Toast?.Type);

        var (_, cardJson) = await cardKit.WaitForRawCardSentAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("project-1", projectService.LastSwitchedProjectId);
        Assert.Equal("release", projectService.LastSwitchedBranch);
        Assert.Contains("show_project_branch_switcher", cardJson);
        Assert.Contains("release", cardJson);
    }

    [Fact]
    public async Task HandleCardActionAsync_DeleteProject_ReturnsImmediateToastAndSendsUpdatedManagerCardAsync()
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
                Branch = "main",
                Status = "ready",
                LocalPath = @"D:\repos\WmsServerV4",
                UpdatedAt = DateTime.Now
            }
        ]);

        var cliExecutor = new RecordingCliExecutorService();
        var cardKit = new StubFeishuCardKitClient();
        var feishuChannel = new StubFeishuChannelService(null);
        var serviceProvider = new TestServiceProvider(userContext, projectService);
        projectService.DeleteProjectDelay = TimeSpan.FromSeconds(2);
        var service = CreateService(cliExecutor, feishuChannel, serviceProvider, cardKit);

        var startedAt = DateTime.UtcNow;
        var response = await service.HandleCardActionAsync(
            """{"action":"delete_project","chat_key":"oc_project_chat","project_id":"project-1"}""",
            chatId: chatId);
        var elapsed = DateTime.UtcNow - startedAt;

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Expected immediate callback response, actual: {elapsed}");
        Assert.Equal(CardActionTriggerResponseDto.ToastSuffix.ToastType.Info, response.Toast?.Type);

        var (_, cardJson) = await cardKit.WaitForRawCardSentAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("project-1", projectService.LastDeletedProjectId);
        Assert.DoesNotContain("WmsServerV4", cardJson);
        Assert.Contains("show_create_project_form", cardJson);
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
        IServiceProvider serviceProvider,
        StubFeishuCardKitClient? cardKit = null)
    {
        var commandService = new FeishuCommandService(
            NullLogger<FeishuCommandService>.Instance,
            new CommandScannerService());

        return new FeishuCardActionService(
            commandService,
            new FeishuHelpCardBuilder(),
            cardKit ?? new StubFeishuCardKitClient(),
            cliExecutor,
            new StubChatSessionService(),
            feishuChannel,
            NullLogger<FeishuCardActionService>.Instance,
            serviceProvider);
    }

    private sealed class RecordingCliExecutorService : ICliExecutorService
    {
        private readonly TaskCompletionSource<string> _executionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, CliToolConfig> _tools = new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-code"] = new CliToolConfig { Id = "claude-code", Name = "Claude Code" },
            ["codex"] = new CliToolConfig { Id = "codex", Name = "Codex" }
        };
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

        public List<CliToolConfig> GetAvailableTools(string? username = null) => _tools.Values.ToList();

        public CliToolConfig? GetTool(string toolId, string? username = null)
            => _tools.TryGetValue(toolId, out var tool) ? tool : null;

        public bool ValidateTool(string toolId, string? username = null) => _tools.ContainsKey(toolId);

        public void CleanupSessionWorkspace(string sessionId) { }

        public void CleanupExpiredWorkspaces() { }

        public string GetSessionWorkspacePath(string sessionId)
        {
            return _workspacePaths.TryGetValue(sessionId, out var workspacePath)
                ? workspacePath
                : throw new InvalidOperationException($"会话 {sessionId} 工作目录不存在或已被清理，请重新创建会话");
        }

        public Task<Dictionary<string, string>> GetToolEnvironmentVariablesAsync(string toolId, string? username = null)
            => Task.FromResult(new Dictionary<string, string>());

        public Task<bool> SaveToolEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars, string? username = null)
            => Task.FromResult(true);

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
        private readonly TaskCompletionSource<(string ChatId, string CardJson)> _rawCardSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FeishuOptions? LastRawCardOptionsOverride { get; private set; }

        public Task<string> CreateCardAsync(string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<bool> UpdateCardAsync(string cardId, string content, int sequence, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => Task.FromResult(true);

        public Task<string> SendCardMessageAsync(string chatId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> ReplyCardMessageAsync(string replyMessageId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<FeishuStreamingHandle> CreateStreamingHandleAsync(string chatId, string? replyMessageId, string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            return Task.FromResult(new FeishuStreamingHandle(
                "card-1",
                "message-1",
                _ => Task.CompletedTask,
                _ => Task.CompletedTask,
                throttleMs: 0));
        }

        public Task<string> SendRawCardAsync(string chatId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            LastRawCardOptionsOverride = optionsOverride;
            _rawCardSent.TrySetResult((chatId, cardJson));
            return Task.FromResult("raw-card-message");
        }

        public Task<string> ReplyElementsCardAsync(string replyMessageId, ElementsCardV2Dto card, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> ReplyRawCardAsync(string replyMessageId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public async Task<(string ChatId, string CardJson)> WaitForRawCardSentAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            using var _ = cts.Token.Register(() => _rawCardSent.TrySetCanceled(cts.Token));
            return await _rawCardSent.Task;
        }
    }

    private sealed class StubFeishuChannelService(string? currentSessionId) : IFeishuChannelService
    {
        private readonly TaskCompletionSource<(string ChatId, string Content, string? Username, string? AppId)> _messageSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsRunning => true;

        public string? CreatedWorkspacePath { get; private set; }

        public string? CreatedToolId { get; private set; }

        public string? LastSwitchedSessionId { get; private set; }

        public string? LastSentChatId { get; private set; }

        public string? LastSentMessage { get; private set; }

        public string? LastSentUsername { get; private set; }

        public string? LastSentAppId { get; private set; }

        public string? LastClosedSessionId { get; private set; }

        public Task<string> SendMessageAsync(string chatId, string content, string? username = null, string? appId = null)
        {
            LastSentChatId = chatId;
            LastSentMessage = content;
            LastSentUsername = username;
            LastSentAppId = appId;
            _messageSent.TrySetResult((chatId, content, username, appId));
            return Task.FromResult("notify-message");
        }

        public Task<string> ReplyMessageAsync(string messageId, string content, string? username = null, string? appId = null)
            => Task.FromResult("reply-message");

        public Task<FeishuStreamingHandle> SendStreamingMessageAsync(string chatId, string initialContent, string? replyToMessageId = null, string? username = null, string? appId = null)
            => throw new NotSupportedException();

        public Task HandleIncomingMessageAsync(FeishuIncomingMessage message) => throw new NotSupportedException();

        public string? GetCurrentSession(string chatKey, string? username = null) => currentSessionId;

        public DateTime? GetSessionLastActiveTime(string sessionId) => DateTime.UtcNow;

        public List<string> GetChatSessions(string chatKey, string? username = null) => currentSessionId == null ? [] : [currentSessionId];

        public bool SwitchCurrentSession(string chatKey, string sessionId, string? username = null)
        {
            LastSwitchedSessionId = sessionId;
            return true;
        }

        public bool CloseSession(string chatKey, string sessionId, string? username = null)
        {
            LastClosedSessionId = sessionId;
            return true;
        }

        public string CreateNewSession(FeishuIncomingMessage message, string? customWorkspacePath = null, string? toolId = null)
        {
            CreatedWorkspacePath = customWorkspacePath;
            CreatedToolId = toolId;
            return "session-new";
        }

        public string? GetSessionUsername(string chatKey) => "luhaiyan";

        public string ResolveToolId(string chatKey, string? username = null) => "claude-code";

        public async Task<(string ChatId, string Content, string? Username, string? AppId)> WaitForMessageAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            using var _ = cts.Token.Register(() => _messageSent.TrySetCanceled(cts.Token));
            return await _messageSent.Task;
        }
    }

    private sealed class TestServiceProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        private readonly StubFeishuUserBindingService _bindingService = new();
        private readonly StubUserFeishuBotConfigService _feishuBotConfigService = new();
        private readonly StubSessionDirectoryService _sessionDirectoryService = new();
        private readonly TestUserContextService _userContextService;
        private readonly TestProjectService _projectService;
        private readonly IChatSessionRepository _chatSessionRepository;

        public TestServiceProvider(
            TestUserContextService? userContextService = null,
            TestProjectService? projectService = null,
            IChatSessionRepository? chatSessionRepository = null)
        {
            _userContextService = userContextService ?? new TestUserContextService();
            _projectService = projectService ?? new TestProjectService(_userContextService);
            _chatSessionRepository = chatSessionRepository ?? new StubChatSessionRepository([]);
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

            if (serviceType == typeof(IUserFeishuBotConfigService))
            {
                return _feishuBotConfigService;
            }

            if (serviceType == typeof(IUserContextService))
            {
                return _userContextService;
            }

            if (serviceType == typeof(IProjectService))
            {
                return _projectService;
            }

            if (serviceType == typeof(IChatSessionRepository))
            {
                return _chatSessionRepository;
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

        public SqlSugarScope GetDB() => throw new NotSupportedException();

        public List<ChatSessionEntity> GetList() => _sessions.ToList();

        public Task<List<ChatSessionEntity>> GetListAsync() => Task.FromResult(GetList());

        public List<ChatSessionEntity> GetList(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => _sessions.AsQueryable().Where(whereExpression).ToList();

        public Task<List<ChatSessionEntity>> GetListAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => Task.FromResult(GetList(whereExpression));

        public int Count(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => _sessions.AsQueryable().Count(whereExpression);

        public Task<int> CountAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => Task.FromResult(Count(whereExpression));

        public PageList<ChatSessionEntity> GetPageList(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page)
            => throw new NotSupportedException();

        public PageList<P> GetPageList<P>(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page)
            => throw new NotSupportedException();

        public Task<PageList<ChatSessionEntity>> GetPageListAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page)
            => throw new NotSupportedException();

        public Task<PageList<P>> GetPageListAsync<P>(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page)
            => throw new NotSupportedException();

        public PageList<ChatSessionEntity> GetPageList(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc)
            => throw new NotSupportedException();

        public Task<PageList<ChatSessionEntity>> GetPageListAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc)
            => throw new NotSupportedException();

        public PageList<P> GetPageList<P>(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc)
            => throw new NotSupportedException();

        public Task<PageList<P>> GetPageListAsync<P>(Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc)
            => throw new NotSupportedException();

        public PageList<ChatSessionEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page)
            => throw new NotSupportedException();

        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page)
            => throw new NotSupportedException();

        public PageList<ChatSessionEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc)
            => throw new NotSupportedException();

        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page, Expression<Func<ChatSessionEntity, object>> orderByExpression = null, OrderByType orderByType = OrderByType.Asc)
            => throw new NotSupportedException();

        public ChatSessionEntity GetById(dynamic id)
            => _sessions.First(x => string.Equals(x.SessionId, id?.ToString(), StringComparison.OrdinalIgnoreCase));

        public Task<ChatSessionEntity> GetByIdAsync(dynamic id)
            => Task.FromResult(_sessions.FirstOrDefault(x => string.Equals(x.SessionId, id?.ToString(), StringComparison.OrdinalIgnoreCase)))!;

        public ChatSessionEntity GetSingle(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => _sessions.AsQueryable().Single(whereExpression);

        public Task<ChatSessionEntity> GetSingleAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => Task.FromResult(GetSingle(whereExpression));

        public ChatSessionEntity GetFirst(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => _sessions.AsQueryable().First(whereExpression);

        public Task<ChatSessionEntity> GetFirstAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => Task.FromResult(GetFirst(whereExpression));

        public bool Insert(ChatSessionEntity obj)
        {
            _sessions.Add(obj);
            return true;
        }

        public Task<bool> InsertAsync(ChatSessionEntity obj) => Task.FromResult(Insert(obj));

        public bool InsertRange(List<ChatSessionEntity> objs)
        {
            _sessions.AddRange(objs);
            return true;
        }

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

        public bool IsAny(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => _sessions.AsQueryable().Any(whereExpression);

        public Task<bool> IsAnyAsync(Expression<Func<ChatSessionEntity, bool>> whereExpression)
            => Task.FromResult(IsAny(whereExpression));

        public Task<List<ChatSessionEntity>> GetByUsernameAsync(string username)
        {
            return Task.FromResult(_sessions
                .Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase))
                .ToList());
        }

        public Task<ChatSessionEntity?> GetByIdAndUsernameAsync(string sessionId, string username)
        {
            return Task.FromResult(_sessions.FirstOrDefault(x =>
                string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<bool> DeleteByIdAndUsernameAsync(string sessionId, string username) => throw new NotSupportedException();

        public Task<List<ChatSessionEntity>> GetByUsernameOrderByUpdatedAtAsync(string username)
        {
            return Task.FromResult(_sessions
                .Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.UpdatedAt)
                .ToList());
        }

        public Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey)
        {
            return Task.FromResult(_sessions
                .Where(x => string.Equals(x.FeishuChatKey, feishuChatKey, StringComparison.OrdinalIgnoreCase))
                .ToList());
        }

        public Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey)
        {
            return Task.FromResult(_sessions.FirstOrDefault(x =>
                string.Equals(x.FeishuChatKey, feishuChatKey, StringComparison.OrdinalIgnoreCase) &&
                x.IsFeishuActive));
        }

        public Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId) => throw new NotSupportedException();

        public Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId) => throw new NotSupportedException();

        public Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null, string? toolId = null)
            => throw new NotSupportedException();
    }

    private sealed class StubFeishuUserBindingService : IFeishuUserBindingService
    {
        public Task<string?> GetBoundWebUsernameAsync(string feishuUserId) => Task.FromResult<string?>(null);

        public Task<bool> IsBoundAsync(string feishuUserId) => Task.FromResult(false);

        public Task<(bool Success, string? ErrorMessage, string? WebUsername)> BindAsync(string feishuUserId, string webUsername, string? appId = null)
            => Task.FromResult((true, (string?)null, (string?)webUsername));

        public Task<bool> UnbindAsync(string feishuUserId) => Task.FromResult(true);

        public Task<List<string>> GetBindableWebUsernamesAsync(string? appId = null) => Task.FromResult(new List<string>());

        public Task<HashSet<string>> GetAllBoundWebUsernamesAsync() => Task.FromResult(new HashSet<string>());
    }

    private sealed class StubUserFeishuBotConfigService : IUserFeishuBotConfigService
    {
        private static readonly FeishuOptions DefaultOptions = new()
        {
            Enabled = true,
            AppId = "shared-app-id",
            AppSecret = "test-app-secret",
            DefaultCardTitle = "AI助手",
            ThinkingMessage = "思考中..."
        };

        public Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username)
            => Task.FromResult<UserFeishuBotConfigEntity?>(null);

        public Task<UserFeishuBotConfigEntity?> GetByAppIdAsync(string appId)
            => Task.FromResult<UserFeishuBotConfigEntity?>(null);

        public Task<UserFeishuBotConfigSaveResult> SaveAsync(UserFeishuBotConfigEntity config)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string username) => Task.FromResult(true);

        public Task<string?> FindConflictingUsernameByAppIdAsync(string username, string? appId)
            => Task.FromResult<string?>(null);

        public FeishuOptions GetSharedDefaults() => DefaultOptions;

        public Task<FeishuOptions> GetEffectiveOptionsAsync(string? username)
            => Task.FromResult(DefaultOptions);

        public Task<FeishuOptions?> GetEffectiveOptionsByAppIdAsync(string? appId)
            => Task.FromResult<FeishuOptions?>(string.IsNullOrWhiteSpace(appId)
                ? null
                : new FeishuOptions
                {
                    Enabled = true,
                    AppId = appId,
                    AppSecret = "test-app-secret",
                    DefaultCardTitle = "AI助手",
                    ThinkingMessage = "思考中..."
                });
    }

    private sealed class StubSessionDirectoryService : ISessionDirectoryService
    {
        private readonly string _allowedRoot = @"D:\VSWorkshop\allowed";

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

        public Task<AllowedDirectoryBrowseResult> BrowseAllowedDirectoriesAsync(string? path, string? username = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.FromResult(new AllowedDirectoryBrowseResult
                {
                    HasConfiguredRoots = true,
                    Roots =
                    [
                        new AllowedDirectoryRootItem
                        {
                            Name = "allowed",
                            Path = _allowedRoot
                        }
                    ]
                });
            }

            if (string.Equals(path, _allowedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new AllowedDirectoryBrowseResult
                {
                    HasConfiguredRoots = true,
                    CurrentPath = _allowedRoot,
                    RootPath = _allowedRoot,
                    Entries =
                    [
                        new AllowedDirectoryBrowseEntry
                        {
                            Name = "src",
                            Path = Path.Combine(_allowedRoot, "src"),
                            IsDirectory = true
                        },
                        new AllowedDirectoryBrowseEntry
                        {
                            Name = "README.md",
                            Path = Path.Combine(_allowedRoot, "README.md"),
                            IsDirectory = false,
                            Size = 42,
                            Extension = ".md"
                        }
                    ]
                });
            }

            if (string.Equals(path, Path.Combine(_allowedRoot, "src"), StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new AllowedDirectoryBrowseResult
                {
                    HasConfiguredRoots = true,
                    CurrentPath = path,
                    ParentPath = _allowedRoot,
                    RootPath = _allowedRoot,
                    Entries = []
                });
            }

            throw new DirectoryNotFoundException(path);
        }
    }

    private sealed class TestUserContextService : IUserContextService
    {
        private string _currentUsername = "default";

        public string GetCurrentUsername() => _currentUsername;

        public string GetCurrentRole() => "admin";

        public bool IsAuthenticated() => true;

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

        public List<string> ProjectBranches { get; set; } = ["main", "release"];

        public string? LastUsernameSeen { get; private set; }

        public CreateProjectRequest? LastCreatedRequest { get; private set; }

        public string? LastSwitchedProjectId { get; private set; }

        public string? LastSwitchedBranch { get; private set; }

        public string? LastDeletedProjectId { get; private set; }

        public TimeSpan GetProjectBranchesDelay { get; set; }

        public TimeSpan SwitchProjectBranchDelay { get; set; }

        public TimeSpan DeleteProjectDelay { get; set; }

        public TimeSpan CloneProjectDelay { get; set; }

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
            LastDeletedProjectId = projectId;
            if (DeleteProjectDelay > TimeSpan.Zero)
            {
                return WaitAndDeleteAsync();
            }

            return Task.FromResult((_projects.Remove(projectId), (string?)null));

            async Task<(bool Success, string? ErrorMessage)> WaitAndDeleteAsync()
            {
                await Task.Delay(DeleteProjectDelay);
                return (_projects.Remove(projectId), (string?)null);
            }
        }

        public Task<(bool Success, string? ErrorMessage)> CloneProjectAsync(string projectId, Action<CloneProgress>? progress = null)
        {
            LastUsernameSeen = _userContextService.GetCurrentUsername();
            if (CloneProjectDelay > TimeSpan.Zero)
            {
                return WaitAndCloneAsync();
            }

            return Task.FromResult((true, (string?)null));

            async Task<(bool Success, string? ErrorMessage)> WaitAndCloneAsync()
            {
                await Task.Delay(CloneProjectDelay);
                return (true, (string?)null);
            }
        }

        public Task<(bool Success, string? ErrorMessage)> PullProjectAsync(string projectId)
            => Task.FromResult((true, (string?)null));

        public Task<(List<string> Branches, string? ErrorMessage)> GetBranchesAsync(GetBranchesRequest request)
            => Task.FromResult<(List<string>, string?)>((ProjectBranches.ToList(), null));

        public Task<(List<string> Branches, string? ErrorMessage)> GetProjectBranchesAsync(string projectId)
        {
            LastUsernameSeen = _userContextService.GetCurrentUsername();
            if (GetProjectBranchesDelay > TimeSpan.Zero)
            {
                return WaitAndReturnBranchesAsync();
            }

            return Task.FromResult<(List<string>, string?)>((ProjectBranches.ToList(), null));

            async Task<(List<string> Branches, string? ErrorMessage)> WaitAndReturnBranchesAsync()
            {
                await Task.Delay(GetProjectBranchesDelay);
                return (ProjectBranches.ToList(), null);
            }
        }

        public async Task<(bool Success, string? ErrorMessage)> SwitchProjectBranchAsync(string projectId, string branch)
        {
            LastUsernameSeen = _userContextService.GetCurrentUsername();
            LastSwitchedProjectId = projectId;
            LastSwitchedBranch = branch;

            if (SwitchProjectBranchDelay > TimeSpan.Zero)
            {
                await Task.Delay(SwitchProjectBranchDelay);
            }

            if (!_projects.TryGetValue(projectId, out var project))
            {
                return (false, (string?)"项目不存在");
            }

            project.Branch = branch;
            project.UpdatedAt = DateTime.Now;
            return (true, (string?)null);
        }

        public string? GetProjectLocalPath(string projectId)
        {
            return _projects.TryGetValue(projectId, out var project) ? project.LocalPath : null;
        }

        public Task<(bool Success, string? ErrorMessage)> CopyProjectToWorkspaceAsync(string projectId, string targetPath, bool includeGit)
            => Task.FromResult((true, (string?)null));
    }
}
