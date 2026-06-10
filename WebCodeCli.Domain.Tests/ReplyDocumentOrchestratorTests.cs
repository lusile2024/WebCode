using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Channels;
using WebCodeCli.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.ChatSession;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Tests;

public sealed class ReplyDocumentOrchestratorTests
{
    private const string ContinueQuestion = "继续";
    private const string FullReplyText = "完整回复";
    private const string FullReplyBody = "完整回复正文";
    private const string FinalReplyText = "结论";
    private const string FinalReplyBody = "结论正文";
    private const string NamedSessionTitle = "商业基础盘";
    private const string UnnamedSessionTitle = "未命名";

    [Fact]
    public async Task QueueCompletedReplyAsync_SkipsWhenBothReplyDocumentsDisabled()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = false,
                FinalReplyDocEnabled = false
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-disabled-chat",
            Username = "luhaiyan",
            Output = FullReplyText,
            FinalAnswerOutput = FinalReplyText
        });

        await WaitUntilAsync(() => harness.ConfigService.UsernameLookupCount == 1);

        Assert.Empty(harness.CardKit.CreatedDocuments);
        Assert.Empty(harness.CardKit.TextMessages);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenFullReplyDocumentEnabled_CreatesOneDocumentAndSendsLink()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true,
                FinalReplyDocEnabled = false
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-full-chat",
            SessionId = "session-1",
            CliThreadId = "thread-1",
            OriginalUserQuestion = "question",
            Username = "luhaiyan",
            Output = FullReplyBody,
            FinalAnswerOutput = FinalReplyBody
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 1);

        var document = Assert.Single(harness.CardKit.CreatedDocuments);
        Assert.Equal("thread-1 question - 完整回复", document.Title);
        Assert.Equal(FullReplyBody, Assert.Single(harness.CardKit.AppendedTexts).Text);
        Assert.Single(harness.CardKit.PermissionUpdates);
        Assert.Single(harness.CardKit.TextMessages);
        Assert.Contains("已生成完整回复文档：", harness.CardKit.TextMessages.Single(), StringComparison.Ordinal);
        Assert.Contains(document.Url, harness.CardKit.TextMessages.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenFinalReplyDocumentEnabled_UsesLiveFinalAnswerOnly()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = false,
                FinalReplyDocEnabled = true
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-final-chat",
            SessionId = "session-1",
            CliThreadId = "thread-2",
            OriginalUserQuestion = ContinueQuestion,
            Username = "luhaiyan",
            Output = "过程说明",
            FinalAnswerOutput = FinalReplyBody
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 1);

        var document = Assert.Single(harness.CardKit.CreatedDocuments);
        Assert.Equal($"thread-2 {ContinueQuestion} - 结论回复", document.Title);
        Assert.Equal(FinalReplyBody, Assert.Single(harness.CardKit.AppendedTexts).Text);
        Assert.Equal(0, harness.HistoryService.FinalAnswerLookupCount);
        Assert.Contains("已生成结论回复文档：", Assert.Single(harness.CardKit.TextMessages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenFinalLiveTextMissing_UsesCodexFallback()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = false,
                FinalReplyDocEnabled = true
            },
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-fallback",
                Username = "luhaiyan",
                ToolId = "codex",
                CliThreadId = "thread-fallback",
                WorkspacePath = @"D:\repo\superpowers"
            });

        harness.HistoryService.FinalAnswerText = "rollout 结论";

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-fallback-chat",
            SessionId = "session-fallback",
            OriginalUserQuestion = ContinueQuestion,
            Username = "luhaiyan",
            Output = "过程说明",
            FinalAnswerOutput = ""
        });

        await WaitUntilAsync(() => harness.CardKit.CreatedDocuments.Count == 1);

        Assert.Equal(1, harness.HistoryService.FinalAnswerLookupCount);
        Assert.Equal("thread-fallback", harness.HistoryService.LastCliThreadId);
        Assert.Equal(@"D:\repo\superpowers", harness.HistoryService.LastWorkspacePath);
        Assert.Equal("rollout 结论", Assert.Single(harness.CardKit.AppendedTexts).Text);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenBothReplyDocumentsEnabled_CreatesTwoDocuments()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true,
                FinalReplyDocEnabled = true
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-both-chat",
            SessionId = "session-1",
            CliThreadId = "thread-both",
            OriginalUserQuestion = "question",
            Username = "luhaiyan",
            Output = FullReplyBody,
            FinalAnswerOutput = FinalReplyBody
        });

        await WaitUntilAsync(() => harness.CardKit.CreatedDocuments.Count == 2);

        Assert.Equal(2, harness.CardKit.CreatedDocuments.Count);
        Assert.Equal(2, harness.CardKit.AppendedTexts.Count);
        Assert.Equal(2, harness.CardKit.PermissionUpdates.Count);
        Assert.Equal(2, harness.CardKit.TextMessages.Count);
        Assert.Contains(harness.CardKit.CreatedDocuments, item => item.Title == "thread-both question - 完整回复");
        Assert.Contains(harness.CardKit.CreatedDocuments, item => item.Title == "thread-both question - 结论回复");
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenDocumentAdminOpenIdConfigured_GrantsAdminPermissionToCreatedDocument()
    {
        var config = new UserFeishuBotConfigEntity
        {
            Username = "luhaiyan",
            FullReplyDocEnabled = true
        };
        SetStringProperty(config, "DocumentAdminOpenId", "ou_doc_admin");

        using var harness = new ReplyDocumentOrchestratorHarness(config);

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-admin-grant-chat",
            SessionId = "session-admin-grant",
            CliThreadId = "thread-admin-grant",
            Username = "luhaiyan",
            OriginalUserQuestion = "continue",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 1);

        var grant = Assert.Single(harness.CardKit.DocumentAdminGrants);
        Assert.Equal(harness.CardKit.CreatedDocuments.Single().DocumentId, grant.DocumentId);
        Assert.Equal("ou_doc_admin", grant.OpenId);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenDocumentAdminOpenIdConfigured_GrantsAdminPermissionToReplyDocumentFolder()
    {
        var config = new UserFeishuBotConfigEntity
        {
            Username = "luhaiyan",
            FullReplyDocEnabled = true
        };
        SetStringProperty(config, "DocumentAdminOpenId", "ou_doc_admin");

        using var harness = new ReplyDocumentOrchestratorHarness(
            config,
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-folder-admin-grant",
                Username = "luhaiyan",
                CliThreadId = "thread-folder-admin-grant",
                Title = NamedSessionTitle
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-folder-admin-grant-chat",
            SessionId = "session-folder-admin-grant",
            CliThreadId = "thread-folder-admin-grant",
            Username = "luhaiyan",
            OriginalUserQuestion = "continue",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 1);

        var folderGrant = Assert.Single(harness.CardKit.FolderAdminGrants);
        Assert.Equal("folder-1", folderGrant.FolderToken);
        Assert.Equal("ou_doc_admin", folderGrant.OpenId);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenGrantingDocumentAdminFails_SendsWarningWithoutBlockingDocumentLink()
    {
        var config = new UserFeishuBotConfigEntity
        {
            Username = "luhaiyan",
            FullReplyDocEnabled = true
        };
        SetStringProperty(config, "DocumentAdminOpenId", "ou_doc_admin");

        using var harness = new ReplyDocumentOrchestratorHarness(config);
        harness.CardKit.GrantDocumentAdminException = new HttpRequestException(
            "API request failed: Status=BadRequest, Content={\"code\":99991672,\"msg\":\"Access denied. One of the following scopes is required: [drive:drive].应用尚未开通所需的应用身份权限：[drive:drive]，点击链接申请并开通任一权限即可：https://open.feishu.cn/app/test/auth?q=drive:drive\"}");

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-admin-grant-warning-chat",
            SessionId = "session-admin-grant-warning",
            CliThreadId = "thread-admin-grant-warning",
            Username = "luhaiyan",
            OriginalUserQuestion = "continue",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 2);

        Assert.Contains("已生成完整回复文档：", harness.CardKit.TextMessages[0], StringComparison.Ordinal);
        Assert.Contains("文档管理员权限授予失败", harness.CardKit.TextMessages[1], StringComparison.Ordinal);
        Assert.Contains("drive:drive", harness.CardKit.TextMessages[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenGrantingFolderAdminFails_SendsWarningWithoutBlockingDocumentLink()
    {
        var config = new UserFeishuBotConfigEntity
        {
            Username = "luhaiyan",
            FullReplyDocEnabled = true
        };
        SetStringProperty(config, "DocumentAdminOpenId", "ou_doc_admin");

        using var harness = new ReplyDocumentOrchestratorHarness(
            config,
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-folder-admin-warning",
                Username = "luhaiyan",
                CliThreadId = "thread-folder-admin-warning",
                Title = NamedSessionTitle
            });

        harness.CardKit.GrantFolderAdminException = new HttpRequestException(
            "API request failed: Status=BadRequest, Content={\"code\":99991672,\"msg\":\"Access denied. One of the following scopes is required: [drive:drive].应用尚未开通所需的应用身份权限：[drive:drive]，点击链接申请并开通任一权限即可：https://open.feishu.cn/app/test/auth?q=drive:drive\"}");

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-folder-admin-warning-chat",
            SessionId = "session-folder-admin-warning",
            CliThreadId = "thread-folder-admin-warning",
            Username = "luhaiyan",
            OriginalUserQuestion = "continue",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 2);

        Assert.Contains("已生成完整回复文档：", harness.CardKit.TextMessages[0], StringComparison.Ordinal);
        Assert.Contains("会话文档文件夹管理员权限授予失败", harness.CardKit.TextMessages[1], StringComparison.Ordinal);
        Assert.Contains("drive:drive", harness.CardKit.TextMessages[1], StringComparison.Ordinal);
        Assert.Equal("folder-1", Assert.Single(harness.CardKit.CreatedDocuments).FolderToken);
        Assert.Empty(harness.CardKit.MovedDocuments);
        Assert.Single(harness.CardKit.DocumentAdminGrants);
        Assert.Empty(harness.CardKit.FolderAdminGrants);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenCliThreadIdMissing_FallsBackToSessionIdInTitle()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-session-fallback-chat",
            SessionId = "session-fallback-id",
            Username = "luhaiyan",
            OriginalUserQuestion = ContinueQuestion,
            Output = FullReplyBody
        });

        await WaitUntilAsync(() => harness.CardKit.CreatedDocuments.Count == 1);

        Assert.Equal($"session-fallback-id {ContinueQuestion} - 完整回复", harness.CardKit.CreatedDocuments.Single().Title);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenSessionTitlePresent_UsesSessionTitleAsFolderName()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            },
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-folder-title",
                Username = "luhaiyan",
                CliThreadId = "thread-folder-title",
                Title = NamedSessionTitle
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-folder-title-chat",
            SessionId = "session-folder-title",
            CliThreadId = "thread-folder-title",
            Username = "luhaiyan",
            OriginalUserQuestion = "补充内容",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 1);

        Assert.Equal([NamedSessionTitle], harness.CardKit.EnsuredFolderNames);
        var document = Assert.Single(harness.CardKit.CreatedDocuments);
        Assert.Equal("folder-1", document.FolderToken);
        Assert.Empty(harness.CardKit.MovedDocuments);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenSessionTitleIsUnnamed_FallsBackToCliThreadIdForFolder()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            },
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-folder-unnamed",
                Username = "luhaiyan",
                CliThreadId = "thread-fallback-folder",
                Title = UnnamedSessionTitle
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-folder-unnamed-chat",
            SessionId = "session-folder-unnamed",
            CliThreadId = "thread-fallback-folder",
            Username = "luhaiyan",
            OriginalUserQuestion = "补充内容",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 1);

        Assert.Equal(["thread-fallback-folder"], harness.CardKit.EnsuredFolderNames);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenTitleAndThreadMissing_FallsBackToSessionIdForFolder()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            },
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-folder-id-fallback",
                Username = "luhaiyan",
                CliThreadId = "",
                Title = " "
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-folder-sessionid-chat",
            SessionId = "session-folder-id-fallback",
            CliThreadId = "",
            Username = "luhaiyan",
            OriginalUserQuestion = "补充内容",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 1);

        Assert.Equal(["session-folder-id-fallback"], harness.CardKit.EnsuredFolderNames);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenDocumentCreationFails_SendsFailureMessageToChat()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            });

        harness.CardKit.CreateDocumentException = new HttpRequestException(
            "API request failed: BadRequest | code=99991672 | missing scopes: docx:document,docx:document:create");

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-create-failed-chat",
            SessionId = "session-create-failed",
            CliThreadId = "thread-create-failed",
            Username = "luhaiyan",
            OriginalUserQuestion = "continue",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 1);

        var failureMessage = Assert.Single(harness.CardKit.TextMessages);
        Assert.Contains("\u751f\u6210\u5931\u8d25", failureMessage, StringComparison.Ordinal);
        Assert.Contains("docx:document", failureMessage, StringComparison.Ordinal);
        Assert.Contains("docx:document:create", failureMessage, StringComparison.Ordinal);
        Assert.Empty(harness.CardKit.AppendedTexts);
        Assert.Empty(harness.CardKit.PermissionUpdates);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenFolderPermissionMissing_CreatesDocumentAndSendsWarningToChat()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            },
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-folder-permission-failed",
                Username = "luhaiyan",
                CliThreadId = "thread-folder-permission-failed",
                Title = "Need Folder"
            });

        harness.CardKit.EnsureFolderException = new HttpRequestException(
            "API request failed: Status=BadRequest, Content={\"code\":99991672,\"msg\":\"Access denied. One of the following scopes is required: [drive:drive, drive:drive.metadata:readonly].应用尚未开通所需的应用身份权限：[drive:drive, drive:drive.metadata:readonly]，点击链接申请并开通任一权限即可：https://open.feishu.cn/app/cli_a929ada764389cd4/auth?q=drive:drive,drive:drive.metadata:readonly&op_from=openapi&token_type=tenant\"}");

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-folder-failed-chat",
            SessionId = "session-folder-permission-failed",
            CliThreadId = "thread-folder-permission-failed",
            Username = "luhaiyan",
            OriginalUserQuestion = "continue",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 2);

        var document = Assert.Single(harness.CardKit.CreatedDocuments);
        var linkMessage = harness.CardKit.TextMessages[0];
        var warningMessage = harness.CardKit.TextMessages[1];
        Assert.Contains("已生成完整回复文档：", linkMessage, StringComparison.Ordinal);
        Assert.Contains(document.Url, linkMessage, StringComparison.Ordinal);
        Assert.Contains("已生成，但归档到会话文档文件夹失败", warningMessage, StringComparison.Ordinal);
        Assert.Contains("drive:drive", warningMessage, StringComparison.Ordinal);
        Assert.Contains("drive:drive.metadata:readonly", warningMessage, StringComparison.Ordinal);
        Assert.Contains("应用尚未开通所需的应用身份权限", warningMessage, StringComparison.Ordinal);
        Assert.Contains("点击链接申请并开通任一权限即可", warningMessage, StringComparison.Ordinal);
        Assert.Contains("https://open.feishu.cn/app/cli_a929ada764389cd4/auth?q=drive:drive,drive:drive.metadata:readonly&op_from=openapi&token_type=tenant", warningMessage, StringComparison.Ordinal);
        Assert.Single(harness.CardKit.AppendedTexts);
        Assert.Single(harness.CardKit.PermissionUpdates);
        Assert.Null(document.FolderToken);
        Assert.Empty(harness.CardKit.MovedDocuments);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenReplyDocumentFolderNotFound_CreatesDocumentAndSendsFolderWarningToChat()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            },
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-folder-not-found",
                Username = "luhaiyan",
                CliThreadId = "thread-folder-not-found",
                Title = "Need Folder"
            });

        harness.CardKit.EnsureFolderException = new HttpRequestException(
            "API request failed: Status=NotFound, Content={\"code\":1061003,\"msg\":\"not found.\",\"error\":{\"log_id\":\"2026060216084302B6962999D06CE4F8A4\"}}");

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-folder-not-found-chat",
            SessionId = "session-folder-not-found",
            CliThreadId = "thread-folder-not-found",
            Username = "luhaiyan",
            OriginalUserQuestion = "continue",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 2);

        var document = Assert.Single(harness.CardKit.CreatedDocuments);
        var linkMessage = harness.CardKit.TextMessages[0];
        var warningMessage = harness.CardKit.TextMessages[1];
        Assert.Contains("已生成完整回复文档：", linkMessage, StringComparison.Ordinal);
        Assert.Contains(document.Url, linkMessage, StringComparison.Ordinal);
        Assert.Contains("已生成，但在定位会话文档文件夹时", warningMessage, StringComparison.Ordinal);
        Assert.Contains("会话文档文件夹", warningMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Status=NotFound", warningMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("\"code\":1061003", warningMessage, StringComparison.Ordinal);
        Assert.Single(harness.CardKit.AppendedTexts);
        Assert.Single(harness.CardKit.PermissionUpdates);
        Assert.Null(document.FolderToken);
        Assert.Empty(harness.CardKit.MovedDocuments);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenCreateInReplyDocumentFolderFailsWithNotFound_CreatesDocumentAndSendsPlacementWarningToChat()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            },
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-folder-move-not-found",
                Username = "luhaiyan",
                CliThreadId = "thread-folder-move-not-found",
                Title = "Need Folder"
            });

        harness.CardKit.CreateDocumentInFolderException = new HttpRequestException(
            "API request failed: Status=NotFound, Content={\"code\":1061003,\"msg\":\"not found.\",\"error\":{\"log_id\":\"202606021724410B11BFB987DE9BFA819F\"}}");

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-folder-create-not-found-chat",
            SessionId = "session-folder-move-not-found",
            CliThreadId = "thread-folder-move-not-found",
            Username = "luhaiyan",
            OriginalUserQuestion = "continue",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 2);

        var document = Assert.Single(harness.CardKit.CreatedDocuments);
        var linkMessage = harness.CardKit.TextMessages[0];
        var warningMessage = harness.CardKit.TextMessages[1];
        Assert.Contains("已生成完整回复文档：", linkMessage, StringComparison.Ordinal);
        Assert.Contains(document.Url, linkMessage, StringComparison.Ordinal);
        Assert.Contains("已生成，但在归档到会话文档文件夹时", warningMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Status=NotFound", warningMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("\"code\":1061003", warningMessage, StringComparison.Ordinal);
        Assert.Single(harness.CardKit.AppendedTexts);
        Assert.Single(harness.CardKit.PermissionUpdates);
        Assert.Single(harness.CardKit.EnsuredFolders);
        Assert.Empty(harness.CardKit.MovedDocuments);
        Assert.Null(document.FolderToken);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_WhenSessionFolderResolved_CreatesDocumentDirectlyInsideFolder()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            },
            session: new ReplyDocumentSessionContext
            {
                SessionId = "session-direct-folder-create",
                Username = "luhaiyan",
                CliThreadId = "thread-direct-folder-create",
                Title = "Need Folder"
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-direct-folder-create-chat",
            SessionId = "session-direct-folder-create",
            CliThreadId = "thread-direct-folder-create",
            Username = "luhaiyan",
            OriginalUserQuestion = "continue",
            Output = "full reply body"
        });

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 1);

        Assert.Single(harness.CardKit.EnsuredFolders);
        var document = Assert.Single(harness.CardKit.CreatedDocuments);
        Assert.Equal("folder-1", document.FolderToken);
        Assert.Empty(harness.CardKit.MovedDocuments);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_SerializesJobsPerChat()
    {
        using var harness = new ReplyDocumentOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                FullReplyDocEnabled = true
            });

        harness.CardKit.BlockFirstCreate = true;

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-serialized-chat",
            SessionId = "session-1",
            Username = "luhaiyan",
            OriginalUserQuestion = "first",
            Output = "first reply"
        });

        await harness.CardKit.FirstCreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyDocumentRequest
        {
            ChatId = "oc-serialized-chat",
            SessionId = "session-2",
            Username = "luhaiyan",
            OriginalUserQuestion = "second",
            Output = "second reply"
        });

        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Single(harness.CardKit.CreatedDocuments);

        harness.CardKit.ReleaseFirstCreate();

        await WaitUntilAsync(() => harness.CardKit.TextMessages.Count == 2);

        Assert.Collection(
            harness.CardKit.CreatedDocuments,
            first => Assert.Equal("session-1 first - 完整回复", first.Title),
            second => Assert.Equal("session-2 second - 完整回复", second.Title));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), "Timed out waiting for the expected condition.");
    }

    private static void SetStringProperty(object target, string propertyName, string value)
    {
        target.GetType().GetProperty(propertyName)?.SetValue(target, value);
    }

    private sealed class ReplyDocumentOrchestratorHarness : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        public ReplyDocumentOrchestratorHarness(
            UserFeishuBotConfigEntity config,
            ReplyDocumentSessionContext? session = null)
        {
            ConfigService = new TrackingUserFeishuBotConfigService(config);
            CardKit = new TrackingFeishuCardKitClient();
            ChatSessionRepository = new TrackingChatSessionRepository(session);
            HistoryService = new TrackingExternalCliSessionHistoryService();

            var services = new ServiceCollection();
            services.AddScoped<IUserFeishuBotConfigService>(_ => ConfigService);
            services.AddScoped<IFeishuCardKitClient>(_ => CardKit);
            services.AddScoped<IChatSessionRepository>(_ => ChatSessionRepository);
            services.AddScoped<IExternalCliSessionHistoryService>(_ => HistoryService);
            services.AddLogging();

            _serviceProvider = services.BuildServiceProvider();
            Orchestrator = new ReplyDocumentOrchestrator(
                _serviceProvider,
                NullLogger<ReplyDocumentOrchestrator>.Instance);
        }

        public TrackingUserFeishuBotConfigService ConfigService { get; }

        public TrackingFeishuCardKitClient CardKit { get; }

        public TrackingChatSessionRepository ChatSessionRepository { get; }

        public TrackingExternalCliSessionHistoryService HistoryService { get; }

        public ReplyDocumentOrchestrator Orchestrator { get; }

        public void Dispose()
        {
            _serviceProvider.Dispose();
        }
    }

    private sealed class TrackingUserFeishuBotConfigService(UserFeishuBotConfigEntity config) : IUserFeishuBotConfigService
    {
        public int UsernameLookupCount { get; private set; }

        public Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username)
        {
            UsernameLookupCount++;
            return Task.FromResult<UserFeishuBotConfigEntity?>(string.Equals(username, config.Username, StringComparison.OrdinalIgnoreCase)
                ? config
                : null);
        }

        public Task<UserFeishuBotConfigEntity?> GetByAppIdAsync(string appId)
            => Task.FromResult<UserFeishuBotConfigEntity?>(null);

        public Task<UserFeishuBotConfigSaveResult> SaveAsync(UserFeishuBotConfigEntity configEntity)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string username) => Task.FromResult(true);

        public Task<string?> FindConflictingUsernameByAppIdAsync(string username, string? appId)
            => Task.FromResult<string?>(null);

        public Task<List<UserFeishuBotConfigEntity>> GetAutoStartCandidatesAsync()
            => Task.FromResult(new List<UserFeishuBotConfigEntity>());

        public Task<bool> UpdateRuntimePreferenceAsync(string username, bool autoStartEnabled, DateTime? lastStartedAt = null)
            => Task.FromResult(true);

        public FeishuOptions GetSharedDefaults() => new()
        {
            Enabled = true,
            AppId = "shared-app-id",
            AppSecret = "shared-secret"
        };

        public Task<FeishuOptions> GetEffectiveOptionsAsync(string? username) => Task.FromResult(GetSharedDefaults());

        public Task<FeishuOptions?> GetEffectiveOptionsByAppIdAsync(string? appId)
            => Task.FromResult<FeishuOptions?>(null);
    }

    private sealed class TrackingChatSessionRepository(ReplyDocumentSessionContext? session) : IChatSessionRepository
    {
        private readonly Dictionary<string, ChatSessionEntity> _sessions = session == null
            ? []
            : new Dictionary<string, ChatSessionEntity>(StringComparer.OrdinalIgnoreCase)
            {
                [session.SessionId] = new ChatSessionEntity
                {
                    SessionId = session.SessionId,
                    Username = session.Username,
                    Title = session.Title,
                    ToolId = session.ToolId,
                    CliThreadId = session.CliThreadId,
                    WorkspacePath = session.WorkspacePath,
                    CcSwitchSnapshotToolId = session.SnapshotToolId
                }
            };

        public SqlSugarScope GetDB() => throw new NotSupportedException();
        public List<ChatSessionEntity> GetList() => _sessions.Values.ToList();
        public Task<List<ChatSessionEntity>> GetListAsync() => Task.FromResult(GetList());
        public List<ChatSessionEntity> GetList(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.Values.AsQueryable().Where(whereExpression).ToList();
        public Task<List<ChatSessionEntity>> GetListAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetList(whereExpression));
        public int Count(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.Values.AsQueryable().Count(whereExpression);
        public Task<int> CountAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(Count(whereExpression));
        public PageList<ChatSessionEntity> GetPageList(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, SqlSugar.OrderByType orderByType = SqlSugar.OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, SqlSugar.OrderByType orderByType = SqlSugar.OrderByType.Asc) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, SqlSugar.OrderByType orderByType = SqlSugar.OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, SqlSugar.OrderByType orderByType = SqlSugar.OrderByType.Asc) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(List<SqlSugar.IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<SqlSugar.IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public PageList<ChatSessionEntity> GetPageList(List<SqlSugar.IConditionalModel> conditionalList, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, SqlSugar.OrderByType orderByType = SqlSugar.OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ChatSessionEntity>> GetPageListAsync(List<SqlSugar.IConditionalModel> conditionalList, PageModel page, System.Linq.Expressions.Expression<Func<ChatSessionEntity, object>> orderByExpression = null, SqlSugar.OrderByType orderByType = SqlSugar.OrderByType.Asc) => throw new NotSupportedException();
        public ChatSessionEntity GetById(dynamic id) => _sessions[(id?.ToString() ?? string.Empty)];
        public Task<ChatSessionEntity> GetByIdAsync(dynamic id)
        {
            ChatSessionEntity? found = _sessions.TryGetValue(id?.ToString() ?? string.Empty, out ChatSessionEntity? stored) ? stored : null;
            return Task.FromResult(found)!;
        }

        public ChatSessionEntity GetSingle(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.Values.AsQueryable().Single(whereExpression);
        public Task<ChatSessionEntity> GetSingleAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetSingle(whereExpression));
        public ChatSessionEntity GetFirst(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.Values.AsQueryable().First(whereExpression);
        public Task<ChatSessionEntity> GetFirstAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(GetFirst(whereExpression));
        public bool Insert(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> InsertAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public bool InsertRange(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public Task<bool> InsertRangeAsync(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public int InsertReturnIdentity(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<int> InsertReturnIdentityAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public long InsertReturnBigIdentity(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<long> InsertReturnBigIdentityAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public bool DeleteByIds(dynamic[] ids) => throw new NotSupportedException();
        public Task<bool> DeleteByIdsAsync(dynamic[] ids) => throw new NotSupportedException();
        public bool Delete(dynamic id) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(dynamic id) => throw new NotSupportedException();
        public bool Delete(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public bool Delete(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => throw new NotSupportedException();
        public bool Update(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public bool UpdateRange(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public bool InsertOrUpdate(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> InsertOrUpdateAsync(ChatSessionEntity obj) => throw new NotSupportedException();
        public Task<bool> UpdateRangeAsync(List<ChatSessionEntity> objs) => throw new NotSupportedException();
        public bool IsAny(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => _sessions.Values.AsQueryable().Any(whereExpression);
        public Task<bool> IsAnyAsync(System.Linq.Expressions.Expression<Func<ChatSessionEntity, bool>> whereExpression) => Task.FromResult(IsAny(whereExpression));
        public Task<List<ChatSessionEntity>> GetByUsernameAsync(string username) => Task.FromResult(_sessions.Values.Where(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<ChatSessionEntity?> GetByIdAndUsernameAsync(string sessionId, string username) => Task.FromResult(_sessions.Values.FirstOrDefault(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> DeleteByIdAndUsernameAsync(string sessionId, string username) => throw new NotSupportedException();
        public Task<List<ChatSessionEntity>> GetByUsernameOrderByUpdatedAtAsync(string username) => throw new NotSupportedException();
        public Task<ChatSessionEntity?> GetByUsernameToolAndCliThreadIdAsync(string username, string toolId, string cliThreadId) => throw new NotSupportedException();
        public Task<ChatSessionEntity?> GetByToolAndCliThreadIdAsync(string toolId, string cliThreadId) => throw new NotSupportedException();
        public Task<bool> UpdateCliThreadIdAsync(string sessionId, string? cliThreadId) => throw new NotSupportedException();
        public Task<bool> UpdateWorkspaceBindingAsync(string sessionId, string? workspacePath, bool isCustomWorkspace) => throw new NotSupportedException();
        public Task<bool> UpdateSessionTitleAsync(string sessionId, string title) => throw new NotSupportedException();
        public Task<bool> UpdateCcSwitchSnapshotAsync(string sessionId, CcSwitchSessionSnapshot snapshot) => throw new NotSupportedException();
        public Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey) => throw new NotSupportedException();
        public Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey) => throw new NotSupportedException();
        public Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId) => throw new NotSupportedException();
        public Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId) => throw new NotSupportedException();
        public Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null, string? toolId = null) => throw new NotSupportedException();
    }

    private sealed class TrackingExternalCliSessionHistoryService : IExternalCliSessionHistoryService
    {
        public int FinalAnswerLookupCount { get; private set; }
        public string? FinalAnswerText { get; set; }
        public string? LastCliThreadId { get; private set; }
        public string? LastWorkspacePath { get; private set; }

        public Task<ExternalCliHistoryResult> GetRecentHistoryAsync(string toolId, string cliThreadId, int maxCount = 20, string? workspacePath = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExternalCliHistoryMessage>> GetRecentMessagesAsync(string toolId, string cliThreadId, int maxCount = 20, string? workspacePath = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> GetCodexFinalAnswerTextAsync(string cliThreadId, string? workspacePath = null, CancellationToken cancellationToken = default)
        {
            FinalAnswerLookupCount++;
            LastCliThreadId = cliThreadId;
            LastWorkspacePath = workspacePath;
            return Task.FromResult(FinalAnswerText);
        }
    }

    private sealed class TrackingFeishuCardKitClient : IFeishuCardKitClient
    {
        private readonly TaskCompletionSource<bool> _releaseFirstCreate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockFirstCreate { get; set; }
        public Exception? CreateDocumentException { get; set; }
        public Exception? EnsureFolderException { get; set; }
        public Exception? MoveDocumentException { get; set; }
        public Exception? CreateDocumentInFolderException { get; set; }
        public Exception? GrantDocumentAdminException { get; set; }
        public Exception? GrantFolderAdminException { get; set; }
        public TaskCompletionSource<bool> FirstCreateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(string Title, string DocumentId, string RootBlockId, string Url, string? FolderToken)> CreatedDocuments { get; } = [];
        public List<(string DocumentId, string BlockId, string Text)> AppendedTexts { get; } = [];
        public List<string> PermissionUpdates { get; } = [];
        public List<string> TextMessages { get; } = [];
        public List<string> EnsuredFolderNames { get; } = [];
        public List<(string FolderName, string FolderToken)> EnsuredFolders { get; } = [];
        public List<(string DocumentId, string FolderToken)> MovedDocuments { get; } = [];
        public List<(string DocumentId, string OpenId)> DocumentAdminGrants { get; } = [];
        public List<(string FolderToken, string OpenId)> FolderAdminGrants { get; } = [];

        public void ReleaseFirstCreate() => _releaseFirstCreate.TrySetResult(true);

        public async Task<FeishuCloudDocumentInfo> CreateCloudDocumentAsync(string title, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null, string? folderToken = null)
        {
            if (!string.IsNullOrWhiteSpace(folderToken) && CreateDocumentInFolderException != null)
            {
                throw CreateDocumentInFolderException;
            }

            if (CreateDocumentException != null)
            {
                throw CreateDocumentException;
            }

            CreatedDocuments.Add((title, $"doc-{CreatedDocuments.Count + 1}", $"root-{CreatedDocuments.Count + 1}", $"https://feishu.cn/docx/doc-{CreatedDocuments.Count + 1}", folderToken));
            FirstCreateStarted.TrySetResult(true);
            if (BlockFirstCreate && CreatedDocuments.Count == 1)
            {
                await _releaseFirstCreate.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            var created = CreatedDocuments[^1];
            return new FeishuCloudDocumentInfo
            {
                DocumentId = created.DocumentId,
                RootBlockId = created.RootBlockId,
                Url = created.Url
            };
        }

        public Task AppendCloudDocumentTextAsync(string documentId, string blockId, string text, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            AppendedTexts.Add((documentId, blockId, text));
            return Task.CompletedTask;
        }

        public Task SetCloudDocumentTenantReadableAsync(string documentId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            PermissionUpdates.Add(documentId);
            return Task.CompletedTask;
        }

        public Task<string> EnsureCloudFolderAsync(string folderName, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            if (EnsureFolderException != null)
            {
                throw EnsureFolderException;
            }

            EnsuredFolderNames.Add(folderName);
            var folderToken = $"folder-{EnsuredFolders.Count + 1}";
            EnsuredFolders.Add((folderName, folderToken));
            return Task.FromResult(folderToken);
        }

        public Task MoveCloudDocumentToFolderAsync(string documentId, string folderToken, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            if (MoveDocumentException != null)
            {
                throw MoveDocumentException;
            }

            MovedDocuments.Add((documentId, folderToken));
            return Task.CompletedTask;
        }

        public Task GrantCloudDocumentMemberFullAccessAsync(string documentId, string openId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            if (GrantDocumentAdminException != null)
            {
                throw GrantDocumentAdminException;
            }

            DocumentAdminGrants.Add((documentId, openId));
            return Task.CompletedTask;
        }

        public Task GrantCloudFolderMemberFullAccessAsync(string folderToken, string openId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            if (GrantFolderAdminException != null)
            {
                throw GrantFolderAdminException;
            }

            FolderAdminGrants.Add((folderToken, openId));
            return Task.CompletedTask;
        }

        public Task<string> SendTextMessageAsync(string chatId, string content, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            TextMessages.Add(content);
            return Task.FromResult($"om_{TextMessages.Count}");
        }

        public Task<string> CreateCardAsync(string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<bool> UpdateCardAsync(string cardId, string content, int sequence, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> SendCardMessageAsync(string chatId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> ReplyCardMessageAsync(string replyMessageId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> ReplyTextMessageAsync(string replyMessageId, string content, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<FeishuDownloadedAttachment> DownloadIncomingAttachmentAsync(FeishuIncomingAttachment attachment, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<FeishuStreamingHandle> CreateStreamingHandleAsync(string chatId, string? replyMessageId, string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null, FeishuStreamingCardChrome? chrome = null) => throw new NotSupportedException();
        public Task<string> SendRawCardAsync(string chatId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> ReplyElementsCardAsync(string replyMessageId, FeishuNetSdk.Im.Dtos.ElementsCardV2Dto card, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<string> ReplyRawCardAsync(string replyMessageId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
        public Task<(byte[] Content, string FileName, string MimeType)> DownloadMessageResourceAsync(string messageId, string fileKey, string resourceType, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null) => throw new NotSupportedException();
    }

    private sealed class ReplyDocumentSessionContext
    {
        public string SessionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string ToolId { get; set; } = string.Empty;
        public string? CliThreadId { get; set; }
        public string? Title { get; set; }
        public string? WorkspacePath { get; set; }
        public string? SnapshotToolId { get; set; }
    }
}
