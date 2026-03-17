using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书渠道服务实现
/// 负责处理飞书消息发送、接收和流式回复
/// 与 CliExecutorService 集成实现 AI 助手功能
/// </summary>
[ServiceDescription(typeof(IFeishuChannelService), ServiceLifetime.Singleton)]
public class FeishuChannelService : BackgroundService, IFeishuChannelService
{
    private readonly FeishuOptions _options;
    private readonly ILogger<FeishuChannelService> _logger;
    private readonly IFeishuCardKitClient _cardKit;
    private readonly IServiceProvider _serviceProvider;
    private FeishuMessageHandler? _messageHandler;
    private readonly ICliExecutorService _cliExecutor;
    private readonly IChatSessionService _chatSessionService;

    private bool _isRunning = false;

    // 飞书渠道默认回退工具 ID（最终还会检查配置和实际可用工具）
    private const string FallbackToolId = "claude-code";

    // 事件去重缓存，避免重复处理相同event_id的消息
    private readonly ConcurrentDictionary<string, DateTime> _processedEventIds = new();
    // 事件缓存过期时间（10分钟，超过这个时间的event_id会被清理）
    private const int EventCacheExpirationMinutes = 10;

    /// <summary>
    /// 服务是否运行中
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 获取聊天的当前活跃会话ID
    /// </summary>
    /// <param name="chatKey">聊天键（格式：feishu:{AppId}:{ChatId}）</param>
    /// <returns>当前会话ID，如果不存在则返回null</returns>
    public string? GetCurrentSession(string chatKey, string? username = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var bindingService = scope.ServiceProvider.GetRequiredService<IFeishuUserBindingService>();
        var sessions = GetValidFeishuSessions(repo, bindingService, chatKey, username);
        var activeSession = sessions.FirstOrDefault(s => s.IsFeishuActive && s.FeishuChatKey == chatKey);
        if (activeSession != null)
        {
            return activeSession.SessionId;
        }

        var latestSession = sessions.OrderByDescending(s => s.UpdatedAt).FirstOrDefault();
        if (latestSession == null || string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return SwitchCurrentSession(chatKey, latestSession.SessionId, username)
            ? latestSession.SessionId
            : null;
    }

    /// <summary>
    /// 获取会话的最后活跃时间
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>最后活跃时间，如果会话不存在则返回null</returns>
    public DateTime? GetSessionLastActiveTime(string sessionId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var session = repo.GetByIdAsync(sessionId).GetAwaiter().GetResult();
        return session?.UpdatedAt;
    }

    /// <summary>
    /// 获取聊天的所有会话ID列表
    /// </summary>
    /// <param name="chatKey">聊天键</param>
    /// <returns>会话ID列表</returns>
    public List<string> GetChatSessions(string chatKey, string? username = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var bindingService = scope.ServiceProvider.GetRequiredService<IFeishuUserBindingService>();
        var sessions = GetValidFeishuSessions(repo, bindingService, chatKey, username);
        return sessions.Select(s => s.SessionId).ToList();
    }

    /// <summary>
    /// 切换聊天的当前活跃会话
    /// </summary>
    /// <param name="chatKey">聊天键</param>
    /// <param name="sessionId">要切换到的会话ID</param>
    /// <returns>是否切换成功</returns>
    public bool SwitchCurrentSession(string chatKey, string sessionId, string? username = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();

        if (string.IsNullOrWhiteSpace(username))
        {
            return repo.SetActiveSessionAsync(chatKey, sessionId).GetAwaiter().GetResult();
        }

        var targetSession = repo.GetByIdAndUsernameAsync(sessionId, username).GetAwaiter().GetResult();
        if (targetSession == null)
        {
            return false;
        }

        var userChatSessions = repo.GetListAsync(x => x.Username == username && x.FeishuChatKey == chatKey)
            .GetAwaiter().GetResult();
        foreach (var session in userChatSessions.Where(x => x.IsFeishuActive))
        {
            session.IsFeishuActive = false;
            session.UpdatedAt = DateTime.Now;
            repo.UpdateAsync(session).GetAwaiter().GetResult();
        }

        targetSession.FeishuChatKey = chatKey;
        targetSession.IsFeishuActive = true;
        targetSession.UpdatedAt = DateTime.Now;
        repo.UpdateAsync(targetSession).GetAwaiter().GetResult();
        return true;
    }

    /// <summary>
    /// 关闭指定会话
    /// </summary>
    /// <param name="chatKey">聊天键</param>
    /// <param name="sessionId">要关闭的会话ID</param>
    /// <returns>是否关闭成功</returns>
    public bool CloseSession(string chatKey, string sessionId, string? username = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();

        bool success;
        if (string.IsNullOrWhiteSpace(username))
        {
            success = repo.CloseFeishuSessionAsync(chatKey, sessionId).GetAwaiter().GetResult();
        }
        else
        {
            var targetSession = repo.GetByIdAndUsernameAsync(sessionId, username).GetAwaiter().GetResult();
            if (targetSession == null)
            {
                return false;
            }

            var wasActive = targetSession.IsFeishuActive && string.Equals(targetSession.FeishuChatKey, chatKey, StringComparison.OrdinalIgnoreCase);
            success = repo.DeleteAsync(targetSession).GetAwaiter().GetResult();
            if (success && wasActive)
            {
                var latestSession = repo.GetByUsernameOrderByUpdatedAtAsync(username).GetAwaiter().GetResult().FirstOrDefault();
                if (latestSession != null)
                {
                    SwitchCurrentSession(chatKey, latestSession.SessionId, username);
                }
            }
        }

        if (success)
        {
            _cliExecutor.CleanupSessionWorkspace(sessionId);
            _logger.LogInformation("Closed session {SessionId} for chat {ChatKey}, user={User}", sessionId, chatKey, username ?? string.Empty);
        }
        return success;
    }

    public FeishuChannelService(
        IOptions<FeishuOptions> options,
        ILogger<FeishuChannelService> logger,
        IFeishuCardKitClient cardKit,
        IServiceProvider serviceProvider,
        ICliExecutorService cliExecutor,
        IChatSessionService chatSessionService)
    {
        _options = options.Value;
        _logger = logger;
        _cardKit = cardKit;
        _serviceProvider = serviceProvider;
        _cliExecutor = cliExecutor;
        _chatSessionService = chatSessionService;
    }

    /// <summary>
    /// 后台服务主执行方法
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Feishu channel is disabled");
            return;
        }

        _logger.LogInformation("Starting Feishu channel service...");

        // 不再订阅静态事件，通过 HandleIncomingMessageAsync 方法处理消息（避免重复处理）

        _isRunning = true;

        _logger.LogInformation("Feishu channel service started (AppId: {AppId})", _options.AppId);

        // 保持运行，等待取消信号
        // WebSocket 连接由外部的 FeishuNetSdk.WebSocket 服务管理
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Feishu channel service cancellation requested");
        }
    }


    /// <summary>
    /// 停止服务
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Feishu channel service...");
        _isRunning = false;
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Feishu channel service stopped");
    }

    /// <summary>
    /// 处理收到的消息
    /// </summary>
    private async Task OnMessageReceivedAsync(FeishuIncomingMessage message)
    {
        try
        {
            _logger.LogInformation(
                "🔥 [FeishuChannel] 收到消息事件: MessageId={MessageId}, ChatId={ChatId}, Content={Content}",
                message.MessageId,
                message.ChatId,
                message.Content);

            string sessionId;
            try
            {
                // 获取当前会话（无会话时抛出异常）
                sessionId = GetCurrentSession(message);
            }
            catch (InvalidOperationException ex)
            {
                // 没有会话时提示用户
                await ReplyMessageAsync(message.MessageId, $"⚠️ {ex.Message}");
                return;
            }

            var toolId = ResolveToolId(message.ChatId, message.SenderName);

            // 添加用户消息到会话
            _chatSessionService.AddMessage(sessionId, new ChatMessage
            {
                Role = "user",
                Content = message.Content,
                CliToolId = toolId,
                CreatedAt = DateTime.UtcNow
            });

            // 创建流式回复，立即显示"思考中"状态
            var handle = await SendStreamingMessageAsync(
                message.ChatId,
                _options.ThinkingMessage,
                message.MessageId);

            _logger.LogInformation(
                "🔥 [FeishuChannel] 流式句柄已创建: CardId={CardId}",
                handle.CardId);

            // 执行 CLI 工具并流式更新卡片
            await ExecuteCliAndStreamAsync(handle, sessionId, toolId, message.Content, message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "🔥 [FeishuChannel] 处理消息失败: {MessageId}",
                message.MessageId);
        }
    }

    /// <summary>
    /// 处理收到的消息（由 FeishuMessageHandler 调用）
    /// </summary>
    /// <param name="message">收到的消息</param>
    public async Task HandleIncomingMessageAsync(FeishuIncomingMessage message)
    {
        // 事件去重检查
        if (!string.IsNullOrEmpty(message.EventId))
        {
            // 清理过期的event_id
            CleanupExpiredEventIds();

            if (_processedEventIds.TryAdd(message.EventId, DateTime.UtcNow))
            {
                _logger.LogDebug("处理新事件: EventId={EventId}", message.EventId);
            }
            else
            {
                _logger.LogInformation("跳过重复事件: EventId={EventId}", message.EventId);
                return;
            }
        }

        _logger.LogInformation("🔥 [FeishuChannel] HandleIncomingMessageAsync 被调用");
        await OnMessageReceivedAsync(message);
    }

    /// <summary>
    /// 清理过期的事件ID缓存
    /// </summary>
    private void CleanupExpiredEventIds()
    {
        var expirationTime = DateTime.UtcNow.AddMinutes(-EventCacheExpirationMinutes);
        var expiredIds = _processedEventIds
            .Where(kv => kv.Value < expirationTime)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in expiredIds)
        {
            _processedEventIds.TryRemove(id, out _);
        }

        if (expiredIds.Count > 0)
        {
            _logger.LogDebug("清理了 {Count} 个过期的事件ID", expiredIds.Count);
        }
    }

    /// <summary>
    /// 获取当前会话
    /// 如果聊天没有活动会话，则抛出异常提示用户手动创建
    /// </summary>
    /// <param name="message">飞书 incoming 消息</param>
    /// <returns>会话ID</returns>
    /// <exception cref="InvalidOperationException">如果没有当前会话则抛出</exception>
    private string GetCurrentSession(FeishuIncomingMessage message)
    {
        var chatKey = message.ChatId.ToLowerInvariant();
        var username = message.SenderName;
        _logger.LogInformation("🔍 [会话匹配] 消息ChatId={ChatId}, ChatKey={ChatKey}, User={User}",
            message.ChatId, chatKey, username);

        var currentSessionId = GetCurrentSession(chatKey, username);
        if (!string.IsNullOrWhiteSpace(currentSessionId))
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
            var currentSession = repo.GetByIdAsync(currentSessionId).GetAwaiter().GetResult();
            if (currentSession != null)
            {
                currentSession.UpdatedAt = DateTime.UtcNow;
                repo.UpdateAsync(currentSession).GetAwaiter().GetResult();
            }

            return currentSessionId;
        }

        throw new InvalidOperationException("当前没有可用会话，请先发送 /feishusessions 命令创建或选择会话。");
    }

    /// <summary>
    /// 创建新会话
    /// </summary>
    /// <param name="message">飞书 incoming 消息</param>
    /// <param name="customWorkspacePath">自定义工作区路径（可选）</param>
    /// <returns>新会话ID</returns>
    public string CreateNewSession(FeishuIncomingMessage message, string? customWorkspacePath = null, string? toolId = null)
    {
        var chatKey = message.ChatId.ToLowerInvariant();
        var username = string.IsNullOrWhiteSpace(message.SenderName) ? "unknown" : message.SenderName;
        var resolvedToolId = NormalizeToolId(toolId) ?? ResolveToolId(chatKey, username);

        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var newSessionId = repo.CreateFeishuSessionAsync(
            chatKey,
            username,
            null,
            resolvedToolId).GetAwaiter().GetResult();

        var session = repo.GetByIdAsync(newSessionId).GetAwaiter().GetResult();
        if (session == null)
        {
            throw new InvalidOperationException($"创建飞书会话后未找到会话记录: {newSessionId}");
        }

        if (!string.IsNullOrWhiteSpace(customWorkspacePath))
        {
            var sessionDirectoryService = scope.ServiceProvider.GetRequiredService<ISessionDirectoryService>();
            sessionDirectoryService.SetSessionWorkspaceAsync(newSessionId, username, customWorkspacePath, true)
                .GetAwaiter().GetResult();
        }
        else
        {
            var workspacePath = _cliExecutor.InitializeSessionWorkspaceAsync(newSessionId).GetAwaiter().GetResult();
            session.WorkspacePath = workspacePath;
            session.IsCustomWorkspace = false;
            session.ToolId = resolvedToolId;
            session.UpdatedAt = DateTime.Now;
            repo.UpdateAsync(session).GetAwaiter().GetResult();
        }

        _logger.LogInformation(
            "Created new session: {SessionId} for chat: {ChatId} (user: {UserName}, tool: {ToolId}, workspace: {WorkspacePath})",
            newSessionId,
            message.ChatId,
            username,
            resolvedToolId,
            customWorkspacePath ?? session.WorkspacePath ?? string.Empty);
        return newSessionId;
    }

    public string? GetSessionUsername(string chatKey)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var bindingService = scope.ServiceProvider.GetRequiredService<IFeishuUserBindingService>();
        var session = GetValidFeishuSessions(repo, bindingService, chatKey).FirstOrDefault();
        return session?.Username;
    }

    public string ResolveToolId(string chatKey, string? username = null)
    {
        var normalizedChatKey = chatKey.ToLowerInvariant();

        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var bindingService = scope.ServiceProvider.GetRequiredService<IFeishuUserBindingService>();
        var sessions = GetValidFeishuSessions(repo, bindingService, normalizedChatKey, username);

        var activeToolId = sessions
            .Where(s => s.IsFeishuActive)
            .Select(s => NormalizeToolId(s.ToolId))
            .FirstOrDefault(IsConfiguredToolAvailable);
        if (!string.IsNullOrWhiteSpace(activeToolId))
        {
            return activeToolId;
        }

        var historicalToolId = sessions
            .Select(s => NormalizeToolId(s.ToolId))
            .FirstOrDefault(IsConfiguredToolAvailable);
        if (!string.IsNullOrWhiteSpace(historicalToolId))
        {
            return historicalToolId;
        }

        return ResolveDefaultToolId();
    }

    private List<ChatSessionEntity> GetValidFeishuSessions(
        IChatSessionRepository repo,
        IFeishuUserBindingService bindingService,
        string chatKey,
        string? username = null)
    {
        CleanupUnboundFeishuSessions(repo, bindingService);

        List<ChatSessionEntity> sessions;
        if (string.IsNullOrWhiteSpace(username))
        {
            sessions = repo.GetByFeishuChatKeyAsync(chatKey).GetAwaiter().GetResult();
        }
        else
        {
            sessions = repo.GetByUsernameOrderByUpdatedAtAsync(username).GetAwaiter().GetResult();
        }

        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    private void CleanupUnboundFeishuSessions(IChatSessionRepository repo, IFeishuUserBindingService bindingService)
    {
        var boundWebUsernames = bindingService.GetAllBoundWebUsernamesAsync().GetAwaiter().GetResult();
        var feishuSessions = repo.GetListAsync(x => x.FeishuChatKey != null).GetAwaiter().GetResult();
        var invalidSessions = feishuSessions
            .Where(session => !boundWebUsernames.Contains(session.Username))
            .ToList();

        foreach (var invalidSession in invalidSessions)
        {
            repo.DeleteAsync(invalidSession).GetAwaiter().GetResult();
            _logger.LogInformation("清理未绑定 Web 用户的飞书旧会话: {SessionId}, User={User}, ChatKey={ChatKey}", invalidSession.SessionId, invalidSession.Username, invalidSession.FeishuChatKey ?? string.Empty);
        }
    }

    /// <summary>
    /// 执行 CLI 工具并流式更新卡片
    /// </summary>
    private async Task ExecuteCliAndStreamAsync(
        FeishuStreamingHandle handle,
        string sessionId,
        string toolId,
        string userPrompt,
        string messageId)
    {
        var outputBuilder = new StringBuilder();
        var assistantMessageBuilder = new StringBuilder();
        var jsonlBuffer = new StringBuilder(); // JSONL 缓冲区，处理不完整的行
        var resolvedToolId = NormalizeToolId(toolId) ?? ResolveDefaultToolId();
        var tool = _cliExecutor.GetTool(resolvedToolId);

        if (tool == null)
        {
            await handle.FinishAsync($"错误：未找到 CLI 工具 '{resolvedToolId}'，请在配置中添加该工具。");
            _logger.LogWarning("CLI tool not found: {ToolId}", resolvedToolId);
            return;
        }

        // 获取适配器（用于解析 JSONL 输出）
        var adapter = _cliExecutor.GetAdapter(tool);
        var useAdapter = adapter != null && _cliExecutor.SupportsStreamParsing(tool);

        _logger.LogDebug(
            "Executing CLI tool: {ToolId} for session: {SessionId}, UseAdapter: {UseAdapter}",
            tool.Id,
            sessionId,
            useAdapter);

        try
        {
            // 执行 CLI 工具并流式处理输出
            await foreach (var chunk in _cliExecutor.ExecuteStreamAsync(sessionId, tool.Id, userPrompt))
            {
                if (chunk.IsError)
                {
                    _logger.LogError(
                        "CLI execution error: {Error}",
                        chunk.ErrorMessage ?? "Unknown error");
                    await handle.FinishAsync($"错误：{chunk.ErrorMessage ?? "执行失败"}");
                    return;
                }

                // 累积原始输出内容
                outputBuilder.Append(chunk.Content);

                // 如果使用适配器，解析 JSONL 并提取助手消息
                string displayContent;
                if (useAdapter)
                {
                    // 解析 JSONL 行并提取助手消息（使用缓冲区处理不完整的行）
                    ProcessJsonlChunk(chunk.Content, adapter!, assistantMessageBuilder, jsonlBuffer);
                    displayContent = assistantMessageBuilder.ToString();

                    // 如果没有助手消息，显示"思考中"
                    if (string.IsNullOrWhiteSpace(displayContent))
                    {
                        displayContent = _options.ThinkingMessage;
                    }
                }
                else
                {
                    // 不使用适配器时，过滤系统消息
                    displayContent = FormatMarkdownOutput(outputBuilder.ToString());
                }

                // 流式更新卡片（节流在 handle 内部处理）
                await handle.UpdateAsync(displayContent);

                _logger.LogDebug(
                    "Streamed chunk: {ContentPreview}...",
                    chunk.Content.Length > 50 ? chunk.Content[..50] : chunk.Content);

                // 如果完成，跳出循环
                if (chunk.IsCompleted)
                {
                    break;
                }
            }

            // 完成流式回复
            string finalOutput;
            if (useAdapter)
            {
                // 使用适配器时，使用提取的助手消息
                finalOutput = assistantMessageBuilder.ToString().Trim();
                if (string.IsNullOrWhiteSpace(finalOutput))
                {
                    finalOutput = ExtractFallbackOutput(outputBuilder.ToString(), adapter!) ?? "无输出";
                }
            }
            else
            {
                // 不使用适配器时，过滤系统消息
                finalOutput = FormatMarkdownOutput(outputBuilder.ToString());
            }

            try
            {
                await ReplyMessageAsync(messageId, "已完成");
            }
            catch (Exception notificationEx)
            {
                _logger.LogWarning(notificationEx, "发送完成通知失败: MessageId={MessageId}", messageId);
            }

            await handle.FinishAsync(finalOutput);

            // 添加助手回复到会话
            _chatSessionService.AddMessage(sessionId, new ChatMessage
            {
                Role = "assistant",
                Content = finalOutput,
                CliToolId = tool.Id,
                IsCompleted = true,
                CreatedAt = DateTime.UtcNow
            });

            // 更新会话最后活动时间
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
            var session = await repo.GetByIdAsync(sessionId);
            if (session != null)
            {
                session.ToolId = tool.Id;
                session.UpdatedAt = DateTime.UtcNow;
                await repo.UpdateAsync(session);
            }

            _logger.LogInformation(
                "CLI execution completed for message: {MessageId}, session: {SessionId}",
                messageId,
                sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CLI execution failed for message: {MessageId}", messageId);
            await handle.FinishAsync($"执行出错：{ex.Message}");
        }
    }

    /// <summary>
    /// 格式化 Markdown 输出
    /// 适用于飞书卡片显示
    /// </summary>
    private static string FormatMarkdownOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "无输出";

        // 过滤系统钩子消息（Claude Code CLI 的内部调试信息）
        // 这些 JSON 格式的消息包含 hook_started、hook_response、SessionStart 等
        // 参考网页端的 JSONL 适配器过滤逻辑
        var lines = output.Split('\n');
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // 跳过空行
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                filteredLines.Add(line);
                continue;
            }

            // 跳过所有系统消息（与网页端行为一致）
            // 系统消息格式: {"type":"system",...}
            // 包括: init、hook_started、hook_response、hook_event 等
            if (trimmedLine.StartsWith("{") && trimmedLine.Contains("\"type\":\"system\""))
            {
                // 过滤所有 type 为 system 的 JSON 消息
                continue;
            }

            filteredLines.Add(line);
        }

        var formatted = string.Join('\n', filteredLines);

        // 移除过多的空行（最多保留连续 2 个空行）
        formatted = System.Text.RegularExpressions.Regex.Replace(
            formatted,
            @"\n{3,}",
            "\n\n");

        // 限制最大长度（飞书卡片有内容限制）
        const int maxLength = 10000;
        if (formatted.Length > maxLength)
        {
            formatted = formatted[..maxLength] + "\n\n... (内容过长，已截断)";
        }

        return formatted.Trim();
    }

    private string ResolveDefaultToolId()
    {
        var configured = NormalizeToolId(_options.DefaultToolId);
        if (IsConfiguredToolAvailable(configured))
        {
            return configured!;
        }

        foreach (var candidate in new[] { FallbackToolId, "codex", "opencode" })
        {
            var normalized = NormalizeToolId(candidate);
            if (IsConfiguredToolAvailable(normalized))
            {
                return normalized!;
            }
        }

        return _cliExecutor.GetAvailableTools().FirstOrDefault()?.Id ?? FallbackToolId;
    }

    private bool IsConfiguredToolAvailable(string? toolId)
    {
        var normalized = NormalizeToolId(toolId);
        return !string.IsNullOrWhiteSpace(normalized) && _cliExecutor.GetTool(normalized) != null;
    }

    private static string? NormalizeToolId(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return null;
        }

        if (toolId.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            return "claude-code";
        }

        if (toolId.Equals("opencode-cli", StringComparison.OrdinalIgnoreCase))
        {
            return "opencode";
        }

        return toolId;
    }

    private static string? ExtractFallbackOutput(string fullOutput, ICliToolAdapter adapter)
    {
        if (string.IsNullOrWhiteSpace(fullOutput))
        {
            return null;
        }

        string? lastUsefulContent = null;
        var lines = fullOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var outputEvent = adapter.ParseOutputLine(line);
            if (outputEvent == null || string.IsNullOrWhiteSpace(outputEvent.Content))
            {
                continue;
            }

            if (outputEvent.EventType is "result" or "error" or "raw" or "assistant" or "assistant:message" or "stream_event")
            {
                lastUsefulContent = outputEvent.Content.Trim();
            }
        }

        return lastUsefulContent;
    }

    /// <summary>
    /// 发送文本消息
    /// </summary>
    public async Task<string> SendMessageAsync(string chatId, string content)
    {
        _logger.LogDebug("Sending message to chat {ChatId}: {Content}", chatId, content);

        // 创建卡片
        var cardId = await _cardKit.CreateCardAsync(content, _options.DefaultCardTitle);

        // 发送卡片消息
        var messageId = await _cardKit.SendCardMessageAsync(chatId, cardId);

        _logger.LogDebug(
            "Message sent: CardId={CardId}, MessageId={MessageId}",
            cardId,
            messageId);

        return messageId;
    }

    /// <summary>
    /// 回复消息
    /// </summary>
    public async Task<string> ReplyMessageAsync(string messageId, string content)
    {
        _logger.LogDebug("Replying to message {MessageId}: {Content}", messageId, content);

        // 创建卡片
        var cardId = await _cardKit.CreateCardAsync(content, _options.DefaultCardTitle);

        // 回复卡片消息
        var replyMessageId = await _cardKit.ReplyCardMessageAsync(messageId, cardId);

        _logger.LogDebug(
            "Reply sent: CardId={CardId}, MessageId={ReplyMessageId}",
            cardId,
            replyMessageId);

        return replyMessageId;
    }

    /// <summary>
    /// 发送流式消息（核心方法）
    /// 立即创建卡片并发送，返回流式句柄用于后续更新
    /// </summary>
    public async Task<FeishuStreamingHandle> SendStreamingMessageAsync(
        string chatId,
        string initialContent,
        string? replyToMessageId = null)
    {
        _logger.LogDebug(
            "Creating streaming message for chat {ChatId} (reply to: {ReplyMessageId})",
            chatId,
            replyToMessageId ?? "none");

        // 通过 CardKit 客户端创建流式句柄
        var handle = await _cardKit.CreateStreamingHandleAsync(
            chatId,
            replyToMessageId,
            initialContent,
            _options.DefaultCardTitle);

        _logger.LogDebug(
            "Streaming handle created: CardId={CardId}, MessageId={MessageId}",
            handle.CardId,
            handle.MessageId);

        return handle;
    }

    /// <summary>
    /// 处理 JSONL 输出块，解析并提取助手消息
    /// 使用缓冲区处理跨 chunk 的不完整 JSON 行
    /// </summary>
    private void ProcessJsonlChunk(string content, ICliToolAdapter adapter, StringBuilder assistantMessageBuilder, StringBuilder jsonlBuffer)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        // 将新内容添加到缓冲区
        jsonlBuffer.Append(content);

        // 处理缓冲区中的完整行
        while (true)
        {
            var bufferContent = jsonlBuffer.ToString();
            var newlineIndex = bufferContent.IndexOf('\n');

            // 如果没有完整的行，等待更多数据
            if (newlineIndex < 0)
            {
                break;
            }

            // 提取完整的行
            var line = bufferContent.Substring(0, newlineIndex).TrimEnd('\r');
            jsonlBuffer.Remove(0, newlineIndex + 1);

            // 处理这一行
            ProcessJsonlLine(line, adapter, assistantMessageBuilder);
        }
    }

    /// <summary>
    /// 处理单行 JSONL
    /// </summary>
    private void ProcessJsonlLine(string line, ICliToolAdapter adapter, StringBuilder assistantMessageBuilder)
    {
        var trimmedLine = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmedLine))
        {
            return;
        }

        // 跳过非 JSON 行
        if (!trimmedLine.StartsWith("{"))
        {
            return;
        }

        try
        {
            // 使用适配器解析输出行
            var outputEvent = adapter.ParseOutputLine(trimmedLine);
            if (outputEvent == null)
            {
                return;
            }

            // 提取助手消息
            var assistantMessage = adapter.ExtractAssistantMessage(outputEvent);
            if (!string.IsNullOrEmpty(assistantMessage))
            {
                assistantMessageBuilder.Append(assistantMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse JSONL line: {Line}", trimmedLine.Length > 100 ? trimmedLine[..100] : trimmedLine);
        }
    }
}
