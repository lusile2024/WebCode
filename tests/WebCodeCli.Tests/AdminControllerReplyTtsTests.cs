using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Controllers;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;
using WebCodeCli.Domain.Repositories.Base.UserAccount;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class AdminControllerReplyTtsTests
{
    [Fact]
    public async Task GetFeishuBotConfig_ReturnsReplyTtsFields()
    {
        var configService = new StubUserFeishuBotConfigService
        {
            ConfigByUsername = new UserFeishuBotConfigEntity
            {
                Username = "alice",
                IsEnabled = true,
                ReplyTtsEnabled = true,
                ReplyTtsVoiceId = "voice-1"
            }
        };

        var controller = CreateController(configService: configService);

        var result = await controller.GetFeishuBotConfig("alice");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserFeishuBotConfigDto>(ok.Value);
        Assert.True(dto.ReplyTtsEnabled);
        Assert.Equal("voice-1", dto.ReplyTtsVoiceId);
    }

    [Fact]
    public async Task SaveFeishuBotConfig_ForwardsReplyTtsFieldsIntoEntity()
    {
        var configService = new StubUserFeishuBotConfigService();
        var controller = CreateController(configService: configService);

        var result = await controller.SaveFeishuBotConfig("alice", new UserFeishuBotConfigDto
        {
            IsEnabled = true,
            ReplyTtsEnabled = true,
            ReplyTtsVoiceId = "voice-9"
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(configService.LastSavedConfig);
        Assert.Equal("alice", configService.LastSavedConfig!.Username);
        Assert.True(configService.LastSavedConfig.ReplyTtsEnabled);
        Assert.Equal("voice-9", configService.LastSavedConfig.ReplyTtsVoiceId);
    }

    private static AdminController CreateController(
        StubUserFeishuBotConfigService? configService = null,
        StubUserFeishuBotRuntimeService? runtimeService = null)
    {
        return new AdminController(
            new StubUserAccountService(),
            new StubUserToolPolicyService(),
            new StubUserWorkspacePolicyService(),
            configService ?? new StubUserFeishuBotConfigService(),
            runtimeService ?? new StubUserFeishuBotRuntimeService(),
            new StubCliExecutorService());
    }

    private sealed class StubUserFeishuBotConfigService : IUserFeishuBotConfigService
    {
        public UserFeishuBotConfigEntity? ConfigByUsername { get; set; }
        public UserFeishuBotConfigEntity? LastSavedConfig { get; private set; }

        public Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username)
        {
            return Task.FromResult(ConfigByUsername);
        }

        public Task<UserFeishuBotConfigEntity?> GetByAppIdAsync(string appId)
        {
            throw new NotSupportedException();
        }

        public Task<UserFeishuBotConfigSaveResult> SaveAsync(UserFeishuBotConfigEntity config)
        {
            LastSavedConfig = config;
            return Task.FromResult(UserFeishuBotConfigSaveResult.Saved());
        }

        public Task<bool> DeleteAsync(string username)
        {
            throw new NotSupportedException();
        }

        public Task<string?> FindConflictingUsernameByAppIdAsync(string username, string? appId)
        {
            throw new NotSupportedException();
        }

        public Task<List<UserFeishuBotConfigEntity>> GetAutoStartCandidatesAsync()
        {
            throw new NotSupportedException();
        }

        public Task<bool> UpdateRuntimePreferenceAsync(string username, bool autoStartEnabled, DateTime? lastStartedAt = null)
        {
            throw new NotSupportedException();
        }

        public FeishuOptions GetSharedDefaults()
        {
            throw new NotSupportedException();
        }

        public Task<FeishuOptions> GetEffectiveOptionsAsync(string? username)
        {
            throw new NotSupportedException();
        }

        public Task<FeishuOptions?> GetEffectiveOptionsByAppIdAsync(string? appId)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubUserFeishuBotRuntimeService : IUserFeishuBotRuntimeService
    {
        public Task<UserFeishuBotRuntimeStatus> GetStatusAsync(string username, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<UserFeishuBotRuntimeStatus> StartAsync(string username, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<UserFeishuBotRuntimeStatus> StopAsync(string username, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new UserFeishuBotRuntimeStatus
            {
                Username = username,
                State = UserFeishuBotRuntimeState.Stopped,
                UpdatedAt = DateTime.Now
            });
        }
    }

    private sealed class StubUserAccountService : IUserAccountService
    {
        public Task EnsureSeedDataAsync() => throw new NotSupportedException();
        public Task<List<UserAccountEntity>> GetAllAsync() => throw new NotSupportedException();
        public Task<List<string>> GetAllUsernamesAsync() => throw new NotSupportedException();
        public Task<UserAccountEntity?> GetByUsernameAsync(string username) => throw new NotSupportedException();
        public Task<UserAccountEntity?> ValidateCredentialsAsync(string username, string password) => throw new NotSupportedException();
        public Task<UserAccountEntity?> CreateOrUpdateAsync(UserAccountEntity account, string? plainPassword = null, bool overwritePassword = true) => throw new NotSupportedException();
        public Task<bool> IsEnabledAsync(string username) => throw new NotSupportedException();
        public Task<bool> IsAdminAsync(string username) => throw new NotSupportedException();
        public Task<bool> SetStatusAsync(string username, string status) => throw new NotSupportedException();
        public Task<bool> UpdateLastLoginAsync(string username, DateTime? lastLoginAt = null) => throw new NotSupportedException();
    }

    private sealed class StubUserToolPolicyService : IUserToolPolicyService
    {
        public Task<bool> IsToolAllowedAsync(string username, string toolId) => throw new NotSupportedException();
        public Task<HashSet<string>> GetAllowedToolIdsAsync(string username, IEnumerable<string> allToolIds) => throw new NotSupportedException();
        public Task<Dictionary<string, bool>> GetPolicyMapAsync(string username, IEnumerable<string> allToolIds) => throw new NotSupportedException();
        public Task<bool> SaveAllowedToolsAsync(string username, IEnumerable<string> allowedToolIds, IEnumerable<string> allToolIds) => throw new NotSupportedException();
    }

    private sealed class StubUserWorkspacePolicyService : IUserWorkspacePolicyService
    {
        public Task<List<string>> GetAllowedDirectoriesAsync(string username) => throw new NotSupportedException();
        public Task<bool> IsPathAllowedAsync(string username, string directoryPath) => throw new NotSupportedException();
        public Task<bool> SaveAllowedDirectoriesAsync(string username, IEnumerable<string> allowedDirectories) => throw new NotSupportedException();
    }

    private sealed class StubCliExecutorService : ICliExecutorService
    {
        public ICliToolAdapter? GetAdapter(CliToolConfig tool) => throw new NotSupportedException();
        public ICliToolAdapter? GetAdapterById(string toolId) => throw new NotSupportedException();
        public bool SupportsStreamParsing(CliToolConfig tool) => throw new NotSupportedException();
        public string? GetCliThreadId(string sessionId) => throw new NotSupportedException();
        public void SetCliThreadId(string sessionId, string threadId) => throw new NotSupportedException();
        public Task ResetSessionRuntimeAsync(string sessionId, bool clearCliThreadId = true, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<StreamOutputChunk> ExecuteStreamAsync(string sessionId, string toolId, string userPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool SupportsLowInterruptionContinue(string toolId) => throw new NotSupportedException();
        public bool CanStartLowInterruptionContinue(string sessionId, string toolId) => throw new NotSupportedException();
        public IAsyncEnumerable<StreamOutputChunk> ExecuteLowInterruptionContinueStreamAsync(string sessionId, string toolId, string? prompt = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public List<CliToolConfig> GetAvailableTools(string? username = null) => [];
        public CliToolConfig? GetTool(string toolId, string? username = null) => throw new NotSupportedException();
        public bool ValidateTool(string toolId, string? username = null) => throw new NotSupportedException();
        public void CleanupSessionWorkspace(string sessionId) => throw new NotSupportedException();
        public void CleanupExpiredWorkspaces() => throw new NotSupportedException();
        public string GetSessionWorkspacePath(string sessionId) => throw new NotSupportedException();
        public Task<Dictionary<string, string>> GetToolEnvironmentVariablesAsync(string toolId, string? username = null) => throw new NotSupportedException();
        public Task<CcSwitchSessionSnapshot?> SyncSessionCcSwitchSnapshotAsync(string sessionId, string? toolId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CodexThreadProviderSyncResult> SyncCodexThreadProviderAsync(string sessionId, string? toolId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SaveToolEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars, string? username = null) => throw new NotSupportedException();
        public byte[]? GetWorkspaceFile(string sessionId, string relativePath) => throw new NotSupportedException();
        public byte[]? GetWorkspaceZip(string sessionId) => throw new NotSupportedException();
        public Task<bool> UploadFileToWorkspaceAsync(string sessionId, string fileName, byte[] fileContent, string? relativePath = null) => throw new NotSupportedException();
        public Task<bool> CreateFolderInWorkspaceAsync(string sessionId, string folderPath) => throw new NotSupportedException();
        public Task<bool> DeleteWorkspaceItemAsync(string sessionId, string relativePath, bool isDirectory) => throw new NotSupportedException();
        public Task<bool> MoveFileInWorkspaceAsync(string sessionId, string sourcePath, string targetPath) => throw new NotSupportedException();
        public Task<bool> CopyFileInWorkspaceAsync(string sessionId, string sourcePath, string targetPath) => throw new NotSupportedException();
        public Task<bool> RenameFileInWorkspaceAsync(string sessionId, string oldPath, string newName) => throw new NotSupportedException();
        public Task<int> BatchDeleteFilesAsync(string sessionId, List<string> relativePaths) => throw new NotSupportedException();
        public Task<string> InitializeSessionWorkspaceAsync(string sessionId, string? projectId = null, bool includeGit = false) => throw new NotSupportedException();
        public void RefreshWorkspaceRootCache() => throw new NotSupportedException();
    }
}
