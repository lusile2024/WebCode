using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.ChatSession;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Domain.Service.Channels;

[ServiceDescription(typeof(IReplyDocumentOrchestrator), ServiceLifetime.Singleton)]
public sealed class ReplyDocumentOrchestrator : IReplyDocumentOrchestrator
{
    private const int MaxTitleLength = 180;
    private const string FullReplySuffix = " - 完整回复";
    private const string FinalReplySuffix = " - 结论回复";
    private const string FullReplyLinkPrefix = "已生成完整回复文档：";
    private const string FinalReplyLinkPrefix = "已生成结论回复文档：";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReplyDocumentOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _chatLocks = new(StringComparer.OrdinalIgnoreCase);

    public ReplyDocumentOrchestrator(
        IServiceProvider serviceProvider,
        ILogger<ReplyDocumentOrchestrator> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task QueueCompletedReplyAsync(FeishuCompletedReplyDocumentRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ChatId))
        {
            throw new ArgumentException("Chat ID is required.", nameof(request));
        }

        _ = Task.Run(() => ProcessQueuedReplyAsync(request));
        return Task.CompletedTask;
    }

    private async Task ProcessQueuedReplyAsync(FeishuCompletedReplyDocumentRequest request)
    {
        var chatLock = _chatLocks.GetOrAdd(request.ChatId.Trim(), static _ => new SemaphoreSlim(1, 1));
        await chatLock.WaitAsync();
        try
        {
            await ProcessReplyCoreAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reply document orchestration failed for chat {ChatId}", request.ChatId);
        }
        finally
        {
            chatLock.Release();
        }
    }

    private async Task ProcessReplyCoreAsync(FeishuCompletedReplyDocumentRequest request)
    {
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IUserFeishuBotConfigService>();
        var userConfig = await ResolveBotConfigAsync(configService, request.Username, request.AppId);
        if (userConfig == null)
        {
            return;
        }

        var cardKitClient = scope.ServiceProvider.GetRequiredService<IFeishuCardKitClient>();
        var fullReplyContent = NormalizeDocumentBody(request.Output);
        var finalReplyContent = NormalizeDocumentBody(await ResolveFinalOnlyOutputAsync(scope.ServiceProvider, request));
        var titlePrefix = BuildTitlePrefix(request);

        if (userConfig.FullReplyDocEnabled && !string.IsNullOrWhiteSpace(fullReplyContent))
        {
            await TryCreateAndSendDocumentAsync(
                cardKitClient,
                configService,
                request,
                $"{titlePrefix}{FullReplySuffix}",
                fullReplyContent,
                FullReplyLinkPrefix);
        }

        if (userConfig.FinalReplyDocEnabled && !string.IsNullOrWhiteSpace(finalReplyContent))
        {
            await TryCreateAndSendDocumentAsync(
                cardKitClient,
                configService,
                request,
                $"{titlePrefix}{FinalReplySuffix}",
                finalReplyContent,
                FinalReplyLinkPrefix);
        }
    }

    private async Task TryCreateAndSendDocumentAsync(
        IFeishuCardKitClient cardKitClient,
        IUserFeishuBotConfigService configService,
        FeishuCompletedReplyDocumentRequest request,
        string title,
        string body,
        string messagePrefix)
    {
        try
        {
            var effectiveOptions = await ResolveEffectiveOptionsAsync(configService, request.Username, request.AppId);
            var document = await cardKitClient.CreateCloudDocumentAsync(
                TruncateTitle(title),
                optionsOverride: effectiveOptions);

            await cardKitClient.AppendCloudDocumentTextAsync(
                document.DocumentId,
                document.RootBlockId,
                body,
                optionsOverride: effectiveOptions);

            await cardKitClient.SetCloudDocumentTenantReadableAsync(
                document.DocumentId,
                optionsOverride: effectiveOptions);

            await cardKitClient.SendTextMessageAsync(
                request.ChatId,
                $"{messagePrefix}{document.Url}",
                optionsOverride: effectiveOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Reply document generation failed for chat {ChatId}, session {SessionId}, title {Title}",
                request.ChatId,
                request.SessionId,
                title);

            await TrySendFailureMessageAsync(
                cardKitClient,
                request,
                BuildFailureMessage(messagePrefix, ex));
        }
    }

    private static string BuildFailureMessage(string messagePrefix, Exception exception)
    {
        var scopeHint = TryExtractPermissionScopes(exception.Message);
        var target = string.Equals(messagePrefix, FinalReplyLinkPrefix, StringComparison.Ordinal)
            ? "\u7ed3\u8bba\u56de\u590d\u6587\u6863"
            : "\u5b8c\u6574\u56de\u590d\u6587\u6863";

        if (!string.IsNullOrWhiteSpace(scopeHint))
        {
            return $"{target}\u751f\u6210\u5931\u8d25\uff1a\u98de\u4e66\u5e94\u7528\u7f3a\u5c11\u6587\u6863\u6743\u9650\uff0c\u8bf7\u5f00\u901a {scopeHint} \u540e\u91cd\u8bd5\u3002";
        }

        var compactReason = BuildCompactErrorReason(exception.Message);
        return string.IsNullOrWhiteSpace(compactReason)
            ? $"{target}\u751f\u6210\u5931\u8d25\uff0c\u8bf7\u68c0\u67e5\u98de\u4e66\u6587\u6863\u6743\u9650\u6216\u670d\u52a1\u65e5\u5fd7\u3002"
            : $"{target}\u751f\u6210\u5931\u8d25\uff1a{compactReason}";
    }

    private static string BuildCompactErrorReason(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var normalized = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 180
            ? normalized
            : normalized[..180].TrimEnd();
    }

    private static string? TryExtractPermissionScopes(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var scopes = new List<string>();
        if (message.Contains("docx:document:create", StringComparison.OrdinalIgnoreCase))
        {
            scopes.Add("docx:document:create");
        }

        if (message.Contains("docx:document", StringComparison.OrdinalIgnoreCase))
        {
            scopes.Add("docx:document");
        }

        return scopes.Count == 0
            ? null
            : string.Join("\u3001", scopes.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task TrySendFailureMessageAsync(
        IFeishuCardKitClient cardKitClient,
        FeishuCompletedReplyDocumentRequest request,
        string message)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IUserFeishuBotConfigService>();
            var effectiveOptions = await ResolveEffectiveOptionsAsync(configService, request.Username, request.AppId);
            await cardKitClient.SendTextMessageAsync(
                request.ChatId,
                message,
                optionsOverride: effectiveOptions);
        }
        catch (Exception notifyEx)
        {
            _logger.LogWarning(
                notifyEx,
                "Reply document failure notification failed for chat {ChatId}, session {SessionId}",
                request.ChatId,
                request.SessionId);
        }
    }

    private static string NormalizeDocumentBody(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return content.Replace("\r\n", "\n").Trim();
    }

    private static string BuildTitlePrefix(FeishuCompletedReplyDocumentRequest request)
    {
        var threadOrSessionId = string.IsNullOrWhiteSpace(request.CliThreadId)
            ? request.SessionId?.Trim()
            : request.CliThreadId.Trim();

        if (string.IsNullOrWhiteSpace(threadOrSessionId))
        {
            threadOrSessionId = "unknown-session";
        }

        var normalizedQuestion = NormalizeQuestionForTitle(request.OriginalUserQuestion);
        if (string.IsNullOrWhiteSpace(normalizedQuestion))
        {
            return threadOrSessionId;
        }

        return $"{threadOrSessionId} {normalizedQuestion}";
    }

    private static string NormalizeQuestionForTitle(string? originalUserQuestion)
    {
        if (string.IsNullOrWhiteSpace(originalUserQuestion))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            originalUserQuestion
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string TruncateTitle(string title)
    {
        if (title.Length <= MaxTitleLength)
        {
            return title;
        }

        return title[..MaxTitleLength].TrimEnd();
    }

    private static async Task<UserFeishuBotConfigEntity?> ResolveBotConfigAsync(
        IUserFeishuBotConfigService configService,
        string? username,
        string? appId)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            var userConfig = await configService.GetByUsernameAsync(username.Trim());
            if (userConfig != null)
            {
                return userConfig;
            }
        }

        if (!string.IsNullOrWhiteSpace(appId))
        {
            return await configService.GetByAppIdAsync(appId.Trim());
        }

        return null;
    }

    private async Task<string?> ResolveFinalOnlyOutputAsync(IServiceProvider serviceProvider, FeishuCompletedReplyDocumentRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FinalAnswerOutput))
        {
            return request.FinalAnswerOutput;
        }

        var fallback = await TryResolveCodexFinalAnswerFallbackAsync(serviceProvider, request);
        return string.IsNullOrWhiteSpace(fallback)
            ? request.FinalAnswerOutput
            : fallback;
    }

    private async Task<string?> TryResolveCodexFinalAnswerFallbackAsync(IServiceProvider serviceProvider, FeishuCompletedReplyDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return null;
        }

        try
        {
            var sessionRepository = serviceProvider.GetService<IChatSessionRepository>();
            var session = sessionRepository == null
                ? null
                : await sessionRepository.GetByIdAsync(request.SessionId.Trim());
            if (session == null)
            {
                return null;
            }

            var effectiveToolId = SessionLaunchOverrideHelper.ResolveEffectiveToolId(
                session.ToolId,
                session.CcSwitchSnapshotToolId);
            if (!string.Equals(effectiveToolId, "codex", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var cliThreadId = string.IsNullOrWhiteSpace(request.CliThreadId)
                ? session.CliThreadId?.Trim()
                : request.CliThreadId.Trim();
            if (string.IsNullOrWhiteSpace(cliThreadId))
            {
                return null;
            }

            var historyService = serviceProvider.GetService<IExternalCliSessionHistoryService>();
            if (historyService == null)
            {
                return null;
            }

            return await historyService.GetCodexFinalAnswerTextAsync(
                cliThreadId,
                workspacePath: session.WorkspacePath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Reply document final-only rollout fallback failed for session {SessionId}",
                request.SessionId);
            return null;
        }
    }

    private static async Task<FeishuOptions> ResolveEffectiveOptionsAsync(
        IUserFeishuBotConfigService configService,
        string? username,
        string? appId)
    {
        if (!string.IsNullOrWhiteSpace(appId))
        {
            var appOptions = await configService.GetEffectiveOptionsByAppIdAsync(appId.Trim());
            if (appOptions != null)
            {
                return appOptions;
            }
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            return await configService.GetEffectiveOptionsAsync(username.Trim());
        }

        return configService.GetSharedDefaults();
    }
}


