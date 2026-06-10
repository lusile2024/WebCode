using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public sealed class ReferencedMarkdownDocumentImporterTests
{
    [Fact]
    public async Task ImportMissingAsync_WhenDocumentAlreadyExistsInFolder_ReusesExistingLink()
    {
        var cardKit = new TrackingFeishuCardKitClient();
        cardKit.ExistingFolderDocumentsByTitle["docs/agent-notes/2026-06-09.md"] = new FeishuCloudDocumentInfo
        {
            DocumentId = "doc-existing",
            RootBlockId = "doc-existing",
            Url = "https://feishu.cn/docx/doc-existing"
        };

        var importer = CreateImporter();
        var candidate = new ReferencedMarkdownDocumentCandidate(
            AbsolutePath: @"D:\VSWorkshop\WebCode\docs\agent-notes\2026-06-09.md",
            RelativePath: "docs/agent-notes/2026-06-09.md",
            Title: "docs/agent-notes/2026-06-09.md");

        await importer.ImportMissingAsync(
            cardKit,
            "oc-chat",
            "fld-session",
            [candidate],
            documentAdminOpenId: null,
            optionsOverride: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(cardKit.ImportedMarkdownDocuments);
        Assert.Contains("已复用Markdown在线文档：[docs/agent-notes/2026-06-09.md](https://feishu.cn/docx/doc-existing)", cardKit.TextMessages.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportMissingAsync_WhenDocumentMissing_ImportsMarkdownAndSendsCreatedLink()
    {
        var workspaceRoot = CreateWorkspaceWithFile("docs/agent-notes/2026-06-09.md", "# note");

        try
        {
            var cardKit = new TrackingFeishuCardKitClient();
            var importer = CreateImporter();
            var candidate = new ReferencedMarkdownDocumentCandidate(
                AbsolutePath: Path.Combine(workspaceRoot, "docs", "agent-notes", "2026-06-09.md"),
                RelativePath: "docs/agent-notes/2026-06-09.md",
                Title: "docs/agent-notes/2026-06-09.md");

            await importer.ImportMissingAsync(
                cardKit,
                "oc-chat",
                "fld-session",
                [candidate],
                documentAdminOpenId: null,
                optionsOverride: null,
                cancellationToken: TestContext.Current.CancellationToken);

            var imported = Assert.Single(cardKit.ImportedMarkdownDocuments);
            Assert.Equal("2026-06-09.md", imported.FileName);
            Assert.Equal("docs/agent-notes/2026-06-09.md", imported.Title);
            Assert.Equal("fld-session", imported.FolderToken);
            Assert.Contains("已生成Markdown在线文档：[docs/agent-notes/2026-06-09.md](", cardKit.TextMessages.Single(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ImportMissingAsync_WhenDocumentAdminConfigured_AppliesReadablePermissionAndAdminGrant()
    {
        var workspaceRoot = CreateWorkspaceWithFile("docs/agent-notes/2026-06-09.md", "# note");

        try
        {
            var cardKit = new TrackingFeishuCardKitClient();
            var importer = CreateImporter();
            var candidate = new ReferencedMarkdownDocumentCandidate(
                AbsolutePath: Path.Combine(workspaceRoot, "docs", "agent-notes", "2026-06-09.md"),
                RelativePath: "docs/agent-notes/2026-06-09.md",
                Title: "docs/agent-notes/2026-06-09.md");

            await importer.ImportMissingAsync(
                cardKit,
                "oc-chat",
                "fld-session",
                [candidate],
                documentAdminOpenId: "ou_doc_admin",
                optionsOverride: null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(["doc-created-1"], cardKit.TenantReadableDocuments);
            Assert.Equal([("doc-created-1", "ou_doc_admin")], cardKit.DocumentAdminGrants);
            Assert.Equal([("fld-session", "ou_doc_admin")], cardKit.FolderAdminGrants);
            Assert.Contains("已生成Markdown在线文档：[docs/agent-notes/2026-06-09.md](", cardKit.TextMessages[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ImportMissingAsync_WhenOneCandidateFails_ContinuesRemainingCandidates()
    {
        var workspaceRoot = CreateWorkspaceWithFile("docs/agent-notes/2026-06-09.md", "# note");

        try
        {
            var cardKit = new TrackingFeishuCardKitClient
            {
                ImportMarkdownException = new InvalidOperationException("导入失败")
            };

            var importer = CreateImporter();
            var missingCandidate = new ReferencedMarkdownDocumentCandidate(
                AbsolutePath: Path.Combine(workspaceRoot, "docs", "agent-notes", "2026-06-09.md"),
                RelativePath: "docs/agent-notes/2026-06-09.md",
                Title: "docs/agent-notes/2026-06-09.md");
            var existingCandidate = new ReferencedMarkdownDocumentCandidate(
                AbsolutePath: Path.Combine(workspaceRoot, "docs", "agent-notes", "2026-06-09.md"),
                RelativePath: "docs/reused.md",
                Title: "docs/reused.md");

            cardKit.ExistingFolderDocumentsByTitle["docs/reused.md"] = new FeishuCloudDocumentInfo
            {
                DocumentId = "doc-reused",
                RootBlockId = "doc-reused",
                Url = "https://feishu.cn/docx/doc-reused"
            };

            await importer.ImportMissingAsync(
                cardKit,
                "oc-chat",
                "fld-session",
                [missingCandidate, existingCandidate],
                documentAdminOpenId: null,
                optionsOverride: null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, cardKit.TextMessages.Count);
            Assert.Contains("Markdown在线文档处理失败", cardKit.TextMessages[0], StringComparison.Ordinal);
            Assert.Contains("已复用Markdown在线文档：[docs/reused.md](https://feishu.cn/docx/doc-reused)", cardKit.TextMessages[1], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ImportMissingAsync_WhenAdminGrantFails_SendsWarningAfterSuccessLink()
    {
        var workspaceRoot = CreateWorkspaceWithFile("docs/agent-notes/2026-06-09.md", "# note");

        try
        {
            var cardKit = new TrackingFeishuCardKitClient
            {
                GrantDocumentAdminException = new InvalidOperationException("grant failed")
            };

            var importer = CreateImporter();
            var candidate = new ReferencedMarkdownDocumentCandidate(
                AbsolutePath: Path.Combine(workspaceRoot, "docs", "agent-notes", "2026-06-09.md"),
                RelativePath: "docs/agent-notes/2026-06-09.md",
                Title: "docs/agent-notes/2026-06-09.md");

            await importer.ImportMissingAsync(
                cardKit,
                "oc-chat",
                "fld-session",
                [candidate],
                documentAdminOpenId: "ou_doc_admin",
                optionsOverride: null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, cardKit.TextMessages.Count);
            Assert.Contains("已生成Markdown在线文档", cardKit.TextMessages[0], StringComparison.Ordinal);
            Assert.Contains("文档管理员权限授予失败", cardKit.TextMessages[1], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ImportMissingAsync_WhenFolderPlacementFails_FallsBackToDefaultDirectoryAndSendsPlacementWarning()
    {
        var workspaceRoot = CreateWorkspaceWithFile("docs/agent-notes/2026-06-09.md", "# note");

        try
        {
            var cardKit = new TrackingFeishuCardKitClient
            {
                ImportMarkdownExceptionWhenFolderTokenProvided = new HttpRequestException("API request failed: Status=NotFound, Content={\"code\":1061003,\"msg\":\"not found.\"}")
            };

            var importer = CreateImporter();
            var candidate = new ReferencedMarkdownDocumentCandidate(
                AbsolutePath: Path.Combine(workspaceRoot, "docs", "agent-notes", "2026-06-09.md"),
                RelativePath: "docs/agent-notes/2026-06-09.md",
                Title: "docs/agent-notes/2026-06-09.md");

            await importer.ImportMissingAsync(
                cardKit,
                "oc-chat",
                "fld-session",
                [candidate],
                documentAdminOpenId: null,
                optionsOverride: null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(
                [("2026-06-09.md", "docs/agent-notes/2026-06-09.md", "fld-session"), ("2026-06-09.md", "docs/agent-notes/2026-06-09.md", null)],
                cardKit.ImportAttempts);
            Assert.Equal(2, cardKit.TextMessages.Count);
            Assert.Contains("已生成Markdown在线文档", cardKit.TextMessages[0], StringComparison.Ordinal);
            Assert.Contains("归档到会话文档文件夹时", cardKit.TextMessages[1], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ImportMissingAsync_WhenFailureWarningSendFails_ContinuesRemainingCandidates()
    {
        var workspaceRoot = CreateWorkspaceWithFile("docs/agent-notes/2026-06-09.md", "# note");

        try
        {
            var cardKit = new TrackingFeishuCardKitClient
            {
                ImportMarkdownException = new InvalidOperationException("导入失败"),
                FailTextMessagePredicate = static content => content.Contains("Markdown在线文档处理失败", StringComparison.Ordinal)
            };

            cardKit.ExistingFolderDocumentsByTitle["docs/reused.md"] = new FeishuCloudDocumentInfo
            {
                DocumentId = "doc-reused",
                RootBlockId = "doc-reused",
                Url = "https://feishu.cn/docx/doc-reused"
            };

            var importer = CreateImporter();
            var failingCandidate = new ReferencedMarkdownDocumentCandidate(
                AbsolutePath: Path.Combine(workspaceRoot, "docs", "agent-notes", "2026-06-09.md"),
                RelativePath: "docs/agent-notes/2026-06-09.md",
                Title: "docs/agent-notes/2026-06-09.md");
            var reusedCandidate = new ReferencedMarkdownDocumentCandidate(
                AbsolutePath: Path.Combine(workspaceRoot, "docs", "agent-notes", "2026-06-09.md"),
                RelativePath: "docs/reused.md",
                Title: "docs/reused.md");

            await importer.ImportMissingAsync(
                cardKit,
                "oc-chat",
                "fld-session",
                [failingCandidate, reusedCandidate],
                documentAdminOpenId: null,
                optionsOverride: null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Single(cardKit.TextMessages);
            Assert.Contains("已复用Markdown在线文档：[docs/reused.md](https://feishu.cn/docx/doc-reused)", cardKit.TextMessages[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static ReferencedMarkdownDocumentImporter CreateImporter()
        => new(NullLogger<ReferencedMarkdownDocumentImporter>.Instance);

    private static string CreateWorkspaceWithFile(string relativePath, string content)
    {
        var root = Path.Combine(Path.GetTempPath(), "webcode-md-importer-" + Guid.NewGuid().ToString("N"));
        var absolutePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, content, Encoding.UTF8);
        return root;
    }

    private sealed class TrackingFeishuCardKitClient : IFeishuCardKitClient
    {
        public Exception? ImportMarkdownException { get; set; }

        public Exception? ImportMarkdownExceptionWhenFolderTokenProvided { get; set; }

        public Exception? GrantDocumentAdminException { get; set; }

        public Exception? GrantFolderAdminException { get; set; }

        public Func<string, bool>? FailTextMessagePredicate { get; set; }

        public Dictionary<string, FeishuCloudDocumentInfo> ExistingFolderDocumentsByTitle { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string FileName, string Title, string FolderToken, string Content)> ImportedMarkdownDocuments { get; } = [];

        public List<(string FileName, string Title, string? FolderToken)> ImportAttempts { get; } = [];

        public List<string> TextMessages { get; } = [];

        public List<string> TenantReadableDocuments { get; } = [];

        public List<(string DocumentId, string OpenId)> DocumentAdminGrants { get; } = [];

        public List<(string FolderToken, string OpenId)> FolderAdminGrants { get; } = [];

        public Task<FeishuCloudDocumentInfo?> FindCloudDocumentInFolderByTitleAsync(
            string folderToken,
            string title,
            CancellationToken cancellationToken = default,
            FeishuOptions? optionsOverride = null)
        {
            return Task.FromResult(ExistingFolderDocumentsByTitle.TryGetValue(title, out var existing) ? existing : null);
        }

        public Task<FeishuCloudDocumentInfo> ImportMarkdownFileAsCloudDocumentAsync(
            string fileName,
            byte[] content,
            string title,
            string? folderToken,
            CancellationToken cancellationToken = default,
            FeishuOptions? optionsOverride = null)
        {
            ImportAttempts.Add((fileName, title, folderToken));

            if (!string.IsNullOrWhiteSpace(folderToken) && ImportMarkdownExceptionWhenFolderTokenProvided != null)
            {
                throw ImportMarkdownExceptionWhenFolderTokenProvided;
            }

            if (ImportMarkdownException != null)
            {
                throw ImportMarkdownException;
            }

            ImportedMarkdownDocuments.Add((fileName, title, folderToken ?? string.Empty, Encoding.UTF8.GetString(content)));
            var number = ImportedMarkdownDocuments.Count;
            return Task.FromResult(new FeishuCloudDocumentInfo
            {
                DocumentId = $"doc-created-{number}",
                RootBlockId = $"doc-created-{number}",
                Url = $"https://feishu.cn/docx/doc-created-{number}"
            });
        }

        public Task SetCloudDocumentTenantReadableAsync(
            string documentId,
            CancellationToken cancellationToken = default,
            FeishuOptions? optionsOverride = null)
        {
            TenantReadableDocuments.Add(documentId);
            return Task.CompletedTask;
        }

        public Task GrantCloudDocumentMemberFullAccessAsync(
            string documentId,
            string openId,
            CancellationToken cancellationToken = default,
            FeishuOptions? optionsOverride = null)
        {
            if (GrantDocumentAdminException != null)
            {
                throw GrantDocumentAdminException;
            }

            DocumentAdminGrants.Add((documentId, openId));
            return Task.CompletedTask;
        }

        public Task GrantCloudFolderMemberFullAccessAsync(
            string folderToken,
            string openId,
            CancellationToken cancellationToken = default,
            FeishuOptions? optionsOverride = null)
        {
            if (GrantFolderAdminException != null)
            {
                throw GrantFolderAdminException;
            }

            FolderAdminGrants.Add((folderToken, openId));
            return Task.CompletedTask;
        }

        public Task<string> SendTextMessageAsync(
            string chatId,
            string content,
            CancellationToken cancellationToken = default,
            FeishuOptions? optionsOverride = null)
        {
            if (FailTextMessagePredicate?.Invoke(content) == true)
            {
                throw new InvalidOperationException("send failed");
            }

            TextMessages.Add(content);
            return Task.FromResult($"om_{TextMessages.Count}");
        }

        public Task<string> CreateCardAsync(string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<FeishuCloudDocumentInfo> CreateCloudDocumentAsync(string title, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null, string? folderToken = null) => throw new NotSupportedException();
        public Task<FeishuStreamingHandle> CreateStreamingHandleAsync(string chatId, string? replyMessageId, string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null, FeishuStreamingCardChrome? chrome = null) => throw new NotSupportedException();
        public Task<FeishuDownloadedAttachment> DownloadIncomingAttachmentAsync(FeishuIncomingAttachment attachment, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<(byte[] Content, string FileName, string MimeType)> DownloadMessageResourceAsync(string messageId, string fileKey, string resourceType, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task MoveCloudDocumentToFolderAsync(string documentId, string folderToken, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> EnsureCloudFolderAsync(string folderName, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> ReplyCardMessageAsync(string replyMessageId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> ReplyElementsCardAsync(string replyMessageId, FeishuNetSdk.Im.Dtos.ElementsCardV2Dto card, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> ReplyRawCardAsync(string replyMessageId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> ReplyTextMessageAsync(string replyMessageId, string content, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> SendCardMessageAsync(string chatId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> SendRawCardAsync(string chatId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task AppendCloudDocumentTextAsync(string documentId, string blockId, string text, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<JsonElement> ConvertMarkdownToCloudDocumentBlocksAsync(string markdown, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task AppendCloudDocumentBlocksAsync(string documentId, string blockId, IReadOnlyCollection<JsonElement> blocks, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> UploadCloudFileAsync(string fileName, byte[] content, string? folderToken, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<bool> UpdateCardAsync(string cardId, string content, int sequence, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
    }
}
