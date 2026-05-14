using Microsoft.Extensions.Logging.Abstractions;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;

namespace WebCodeCli.Domain.Tests;

public class MessageSubmissionServiceTests
{
    [Fact]
    public async Task PrepareAsync_WhenTextIsEmptyAndAttachmentsExist_ThrowsRequiredValidationError()
    {
        var workspaceRoot = CreateWorkspaceRoot();

        try
        {
            var service = CreateService(workspaceRoot);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(new MessageDraft
            {
                DraftId = "submission-123",
                SessionId = "session-123",
                ToolId = "codex",
                Channel = MessageSubmissionChannel.Web,
                Text = "   ",
                Attachments =
                [
                    new MessageDraftAttachmentInput
                    {
                        FileName = "diagram.png",
                        ContentType = "image/png",
                        Content = [0x01]
                    }
                ]
            }));

            Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteWorkspaceRoot(workspaceRoot);
        }
    }

    [Fact]
    public async Task PrepareAsync_WhenTextAndMixedCapabilitiesAttachmentsProvided_ReturnsPreparedSubmissionWithPartialDowngradeWarning()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        const string draftId = "submission:123/with?chars";
        const string normalizedStagingRoot = ".webcode/message-inputs/submission_123_with_chars";

        try
        {
            var service = CreateService(workspaceRoot);

            var prepared = await service.PrepareAsync(new MessageDraft
            {
                DraftId = draftId,
                SessionId = "session-123",
                ToolId = "codex",
                Channel = MessageSubmissionChannel.Web,
                Text = "  Review all attached files  ",
                Attachments =
                [
                    new MessageDraftAttachmentInput
                    {
                        FileName = "diagram.png",
                        ContentType = "image/png",
                        Content = [0x01, 0x02]
                    },
                    new MessageDraftAttachmentInput
                    {
                        FileName = "notes.pdf",
                        ContentType = "application/pdf",
                        Content = [0x03, 0x04, 0x05]
                    }
                ]
            });

            Assert.Equal("Review all attached files", prepared.UserMessage.Content);
            Assert.True(prepared.UserMessage.IsCompleted);
            Assert.Equal(2, prepared.UserMessage.Attachments.Count);
            Assert.Equal("session-123", prepared.SessionId);
            Assert.Equal("codex", prepared.ToolId);
            Assert.Equal(2, prepared.Attachments.Count);
            Assert.Equal(normalizedStagingRoot, prepared.StagingRootRelativePath);
            Assert.All(
                prepared.Attachments,
                attachment => Assert.StartsWith(normalizedStagingRoot + "/", attachment.WorkspaceRelativePath, StringComparison.Ordinal));
            Assert.Single(prepared.ExecutionRequest.NativeAttachments);
            Assert.Single(prepared.ExecutionRequest.ReferenceAttachments);
            Assert.Contains(prepared.Warnings, warning => warning.Code == "partial-downgrade");
            Assert.Contains(prepared.ExecutionRequest.Warnings, warning => warning.Code == "partial-downgrade");
        }
        finally
        {
            DeleteWorkspaceRoot(workspaceRoot);
        }
    }

    private static MessageSubmissionService CreateService(string workspaceRoot)
    {
        var cliExecutor = new StubCliExecutorService(workspaceRoot);
        var stagingService = new AttachmentStagingService(cliExecutor, NullLogger<AttachmentStagingService>.Instance);
        return new MessageSubmissionService(
            cliExecutor,
            new CliAdapterFactory(),
            stagingService,
            NullLogger<MessageSubmissionService>.Instance);
    }

    private static string CreateWorkspaceRoot()
    {
        var workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "WebCodeCli.Domain.Tests",
            nameof(MessageSubmissionServiceTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        return workspaceRoot;
    }

    private static void DeleteWorkspaceRoot(string workspaceRoot)
    {
        if (Directory.Exists(workspaceRoot))
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private sealed class StubCliExecutorService(string workspaceRoot) : ICliExecutorService
    {
        private static readonly CliToolConfig CodexTool = new()
        {
            Id = "codex",
            Name = "Codex",
            Command = "codex"
        };

        public ICliToolAdapter? GetAdapter(CliToolConfig tool) => new CodexAdapter();
        public ICliToolAdapter? GetAdapterById(string toolId) => string.Equals(toolId, "codex", StringComparison.OrdinalIgnoreCase) ? new CodexAdapter() : null;
        public bool SupportsStreamParsing(CliToolConfig tool) => true;
        public string? GetCliThreadId(string sessionId) => null;
        public void SetCliThreadId(string sessionId, string threadId) => throw new NotSupportedException();
        public Task ResetSessionRuntimeAsync(string sessionId, bool clearCliThreadId = true, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopSessionExecutionAsync(string sessionId, string? toolId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<StreamOutputChunk> ExecuteStreamAsync(string sessionId, string toolId, string userPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool SupportsLowInterruptionContinue(string toolId) => false;
        public bool CanStartLowInterruptionContinue(string sessionId, string toolId) => false;
        public IAsyncEnumerable<StreamOutputChunk> ExecuteLowInterruptionContinueStreamAsync(string sessionId, string toolId, string? prompt = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public List<CliToolConfig> GetAvailableTools(string? username = null) => [CodexTool];
        public CliToolConfig? GetTool(string toolId, string? username = null) => string.Equals(toolId, "codex", StringComparison.OrdinalIgnoreCase) ? CodexTool : null;
        public bool ValidateTool(string toolId, string? username = null) => string.Equals(toolId, "codex", StringComparison.OrdinalIgnoreCase);
        public void CleanupSessionWorkspace(string sessionId) => throw new NotSupportedException();
        public void CleanupExpiredWorkspaces() => throw new NotSupportedException();
        public string GetSessionWorkspacePath(string sessionId) => workspaceRoot;
        public Task<Dictionary<string, string>> GetToolEnvironmentVariablesAsync(string toolId, string? username = null) => Task.FromResult(new Dictionary<string, string>());
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
