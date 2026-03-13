using System.Text.Json;
using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Im.Dtos;
using WebCodeCli.Domain.Domain.Model.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书卡片回调处理服务
/// 处理卡片按钮点击的回调逻辑
/// </summary>
public class FeishuCardActionService
{
    private readonly FeishuCommandService _commandService;
    private readonly FeishuHelpCardBuilder _cardBuilder;
    private readonly IFeishuCardKitClient _cardKit;
    private readonly ICliExecutorService _cliExecutor;
    private readonly IChatSessionService _chatSessionService;
    private readonly IFeishuChannelService _feishuChannel;
    private readonly ILogger<FeishuCardActionService> _logger;
    private readonly IServiceProvider _serviceProvider;

    // 默认使用的 CLI 工具 ID
    private const string DefaultToolId = "claude-code";

    // 会话映射（从 FeishuChannelService 复制）
    private readonly Dictionary<string, string> _sessionMappings = new();

    // 待确认关闭的临时会话：sessionId -> 确认有效期截止时间
    private readonly Dictionary<string, DateTime> _pendingCloseSessions = new();

    public FeishuCardActionService(
        FeishuCommandService commandService,
        FeishuHelpCardBuilder cardBuilder,
        IFeishuCardKitClient cardKit,
        ICliExecutorService cliExecutor,
        IChatSessionService chatSessionService,
        IFeishuChannelService feishuChannel,
        ILogger<FeishuCardActionService> logger,
        IServiceProvider serviceProvider)
    {
        _commandService = commandService;
        _cardBuilder = cardBuilder;
        _cardKit = cardKit;
        _cliExecutor = cliExecutor;
        _chatSessionService = chatSessionService;
        _feishuChannel = feishuChannel;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 处理卡片回调动作（核心逻辑）
    /// </summary>
    /// <param name="actionJson">action.value JSON字符串</param>
    /// <param name="formValue">form_value（execute_command时使用）</param>
    /// <param name="chatId">聊天ID（用于执行命令时创建会话）</param>
    /// <param name="inputValues">输入框值缓存</param>
    /// <returns>回调响应对象（包含toast和card）</returns>
    public async Task<CardActionTriggerResponseDto> HandleCardActionAsync(
        string actionJson,
        Dictionary<string, object>? formValue = null,
        string? chatId = null,
        string? inputValues = null)
    {
        try
        {
            _logger.LogInformation("🔥 [FeishuHelp] 收到卡片回调: ActionJson={ActionJson}",
                actionJson.Length > 200 ? actionJson[..200] + "..." : actionJson);

            var action = JsonSerializer.Deserialize<FeishuHelpCardAction>(actionJson);
            if (action == null)
            {
                _logger.LogWarning("🔥 [FeishuHelp] 无法解析 action: {ActionJson}", actionJson);
                return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 无法解析动作", "error");
            }

            _logger.LogInformation("🔥 [FeishuHelp] 卡片回调: Action={Action}, CommandId={CommandId}",
                action.Action, action.CommandId);

            // 将 formValue 转换为 JsonElement
            JsonElement? formValueElement = null;
            if (formValue != null)
            {
                formValueElement = JsonSerializer.SerializeToElement(formValue);
            }

            switch (action.Action)
            {
                case "refresh_commands":
                    return await HandleRefreshCommandsAsync(chatId);
                case "select_command":
                    return await HandleSelectCommandAsync(action.CommandId, chatId);
                case "back_to_list":
                    return await HandleBackToListAsync(chatId);
                case "execute_command":
                    return await HandleExecuteCommandAsync(formValueElement, action.Command, chatId, inputValues);
                case "switch_session":
                    return await HandleSwitchSessionAsync(action.SessionId, action.ChatKey);
                case "close_session":
                    return await HandleCloseSessionAsync(action.SessionId, action.ChatKey);
                case "create_session":
                    return await HandleCreateSessionAsync(action.ChatKey, chatId);
                case "open_session_manager":
                    return await HandleOpenSessionManagerAsync(chatId);
                default:
                    return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 未知动作", "error");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [FeishuHelp] 处理卡片回调失败");
            return _cardBuilder.BuildCardActionToastOnlyResponse($"❌ 处理失败: {ex.Message}", "error");
        }
    }

    /// <summary>
    /// 处理刷新命令 - 直接在回调响应中返回新卡片
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleRefreshCommandsAsync(string? chatId)
    {
        await _commandService.RefreshCommandsAsync();
        var categories = await _commandService.GetCategorizedCommandsAsync();
        var card = _cardBuilder.BuildCommandListCardV2(categories, showRefreshButton: false);
        _logger.LogInformation("✅ [FeishuHelp] 返回命令列表卡片（回调响应）");
        return _cardBuilder.BuildCardActionResponseV2(card, "🔄 命令列表已更新", "info");
    }

    /// <summary>
    /// 处理选择命令 - 直接在回调响应中返回执行卡片（卡片2）
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleSelectCommandAsync(string? commandId, string? chatId)
    {
        if (string.IsNullOrEmpty(chatId))
        {
            _logger.LogWarning("❌ [FeishuHelp] 没有 chatId，无法显示执行卡片");
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 缺少 chatId", "error");
        }

        if (string.IsNullOrEmpty(commandId))
        {
            var categories = await _commandService.GetCategorizedCommandsAsync();
            var card = _cardBuilder.BuildCommandListCardV2(categories);
            return _cardBuilder.BuildCardActionResponseV2(card, "📋 显示命令列表", "info");
        }

        // 特殊处理：会话管理命令直接返回会话管理卡片，不进入执行界面
        if (commandId == "feishusessions")
        {
            return await HandleOpenSessionManagerAsync(chatId);
        }

        var command = await _commandService.GetCommandAsync(commandId);
        if (command == null)
        {
            var categories = await _commandService.GetCategorizedCommandsAsync();
            var card = _cardBuilder.BuildCommandListCardV2(categories);
            _logger.LogWarning("❌ [FeishuHelp] 命令不存在");
            return _cardBuilder.BuildCardActionResponseV2(card, "❌ 命令不存在", "warning");
        }

        var executeCard = _cardBuilder.BuildExecuteCardV2(command);
        _logger.LogInformation("📋 [FeishuHelp] 返回执行卡片（卡片2）: {CommandName}", command.Name);
        return _cardBuilder.BuildCardActionResponseV2(executeCard, "", "info");
    }

    /// <summary>
    /// 处理返回列表 - 直接在回调响应中返回命令列表卡片
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleBackToListAsync(string? chatId)
    {
        if (string.IsNullOrEmpty(chatId))
        {
            _logger.LogWarning("❌ [FeishuHelp] 没有 chatId，无法返回命令列表");
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 缺少 chatId", "error");
        }

        var categories = await _commandService.GetCategorizedCommandsAsync();
        var card = _cardBuilder.BuildCommandListCardV2(categories);
        _logger.LogInformation("📋 [FeishuHelp] 返回命令列表卡片（回调响应）");
        return _cardBuilder.BuildCardActionResponseV2(card, "", "info");
    }

    /// <summary>
    /// 处理执行命令
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleExecuteCommandAsync(JsonElement? formValue, string? commandFromAction, string? chatId, string? inputValues = null)
    {
        if (string.IsNullOrEmpty(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 缺少必要参数", "error");
        }

        // 优先从 form_value 获取命令输入
        string? commandInput = null;
        if (formValue != null)
        {
            commandInput = formValue.Value.TryGetProperty("command_input", out var inputEl)
                ? inputEl.GetString()
                : null;
        }

        // 从输入值缓存中获取（优先级最高）
        if (string.IsNullOrEmpty(commandInput) && inputValues != null)
        {
            commandInput = inputValues;
            _logger.LogInformation("🔥 [FeishuHelp] 从缓存获取命令输入: {Command}", commandInput);
        }

        // 如果都没有，从 action 中获取
        if (string.IsNullOrEmpty(commandInput))
        {
            commandInput = commandFromAction;
        }

        if (string.IsNullOrEmpty(commandInput))
        {
            _logger.LogWarning("⚠️ [FeishuHelp] 请输入命令");
            return _cardBuilder.BuildCardActionToastOnlyResponse("⚠️ 请输入命令", "warning");
        }

        _logger.LogInformation("🚀 [FeishuHelp] 执行命令: {Command}", commandInput);

        // 立即返回 toast 响应
        var toastResponse = _cardBuilder.BuildCardActionToastOnlyResponse("🚀 开始执行命令...", "info");

        // 在后台执行命令（不等待）
        _ = Task.Run(async () =>
        {
            try
            {
                // 获取或创建会话
                var sessionId = GetOrCreateSession(chatId);

                // 添加用户消息到会话
                _chatSessionService.AddMessage(sessionId, new Domain.Model.ChatMessage
                {
                    Role = "user",
                    Content = commandInput,
                    CreatedAt = DateTime.Now
                });

                // 创建流式回复
                var handle = await _cardKit.CreateStreamingHandleAsync(
                    chatId,
                    null,
                    "思考中...",
                    "AI 助手");

                _logger.LogInformation(
                    "🔥 [FeishuHelp] 流式句柄已创建: CardId={CardId}",
                    handle.CardId);

                // 执行 CLI 工具并流式更新卡片
                await ExecuteCliAndStreamAsync(handle, sessionId, commandInput);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [FeishuHelp] 执行命令失败");
            }
        });

        return toastResponse;
    }

    /// <summary>
    /// 获取或创建会话
    /// </summary>
    private string GetOrCreateSession(string chatId)
    {
        var chatKey = $"feishu:help:{chatId}";

        if (_sessionMappings.TryGetValue(chatKey, out var existingSessionId))
        {
            _logger.LogDebug("Using existing session: {SessionId} for chat: {ChatId}", existingSessionId, chatId);
            return existingSessionId;
        }

        var newSessionId = Guid.NewGuid().ToString();
        _sessionMappings[chatKey] = newSessionId;

        _logger.LogInformation(
            "Created new session: {SessionId} for chat: {ChatId}",
            newSessionId,
            chatId);

        return newSessionId;
    }

    /// <summary>
    /// 执行 CLI 工具并流式更新卡片（从 FeishuChannelService 复制）
    /// </summary>
    private async Task ExecuteCliAndStreamAsync(
        FeishuStreamingHandle handle,
        string sessionId,
        string userPrompt)
    {
        var outputBuilder = new System.Text.StringBuilder();
        var assistantMessageBuilder = new System.Text.StringBuilder();
        var jsonlBuffer = new System.Text.StringBuilder();
        var tool = _cliExecutor.GetTool(DefaultToolId);

        if (tool == null)
        {
            await handle.FinishAsync($"错误：未找到 CLI 工具 '{DefaultToolId}'，请在配置中添加该工具。");
            _logger.LogWarning("CLI tool not found: {ToolId}", DefaultToolId);
            return;
        }

        var adapter = _cliExecutor.GetAdapter(tool);
        var useAdapter = adapter != null && _cliExecutor.SupportsStreamParsing(tool);

        _logger.LogDebug(
            "Executing CLI tool: {ToolId} for session: {SessionId}, UseAdapter: {UseAdapter}",
            tool.Id,
            sessionId,
            useAdapter);

        try
        {
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

                outputBuilder.Append(chunk.Content);

                string displayContent;
                if (useAdapter)
                {
                    ProcessJsonlChunk(chunk.Content, adapter!, assistantMessageBuilder, jsonlBuffer);
                    displayContent = assistantMessageBuilder.ToString();

                    if (string.IsNullOrWhiteSpace(displayContent))
                    {
                        displayContent = "思考中...";
                    }
                }
                else
                {
                    displayContent = FormatMarkdownOutput(outputBuilder.ToString());
                }

                await handle.UpdateAsync(displayContent);

                if (chunk.IsCompleted)
                {
                    break;
                }
            }

            string finalOutput;
            if (useAdapter)
            {
                finalOutput = assistantMessageBuilder.ToString().Trim();
                if (string.IsNullOrWhiteSpace(finalOutput))
                {
                    finalOutput = "无输出";
                }
            }
            else
            {
                finalOutput = FormatMarkdownOutput(outputBuilder.ToString());
            }

            await handle.FinishAsync(finalOutput);

            _chatSessionService.AddMessage(sessionId, new Domain.Model.ChatMessage
            {
                Role = "assistant",
                Content = finalOutput,
                CliToolId = DefaultToolId,
                IsCompleted = true,
                CreatedAt = DateTime.Now
            });

            _logger.LogInformation(
                "CLI execution completed for session: {SessionId}",
                sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CLI execution failed for session: {SessionId}", sessionId);
            await handle.FinishAsync($"执行出错：{ex.Message}");
        }
    }

    /// <summary>
    /// 处理 JSONL 输出块
    /// </summary>
    private void ProcessJsonlChunk(string content, ICliToolAdapter adapter, System.Text.StringBuilder assistantMessageBuilder, System.Text.StringBuilder jsonlBuffer)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        jsonlBuffer.Append(content);

        while (true)
        {
            var bufferContent = jsonlBuffer.ToString();
            var newlineIndex = bufferContent.IndexOf('\n');

            if (newlineIndex < 0)
            {
                break;
            }

            var line = bufferContent.Substring(0, newlineIndex).TrimEnd('\r');
            jsonlBuffer.Remove(0, newlineIndex + 1);

            ProcessJsonlLine(line, adapter, assistantMessageBuilder);
        }
    }

    /// <summary>
    /// 处理单行 JSONL
    /// </summary>
    private void ProcessJsonlLine(string line, ICliToolAdapter adapter, System.Text.StringBuilder assistantMessageBuilder)
    {
        var trimmedLine = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmedLine))
        {
            return;
        }

        if (!trimmedLine.StartsWith("{"))
        {
            return;
        }

        try
        {
            var outputEvent = adapter.ParseOutputLine(trimmedLine);
            if (outputEvent == null)
            {
                return;
            }

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

    /// <summary>
    /// 格式化 Markdown 输出
    /// </summary>
    private static string FormatMarkdownOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "无输出";

        var lines = output.Split('\n');
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                filteredLines.Add(line);
                continue;
            }

            if (trimmedLine.StartsWith("{") && trimmedLine.Contains("\"type\":\"system\""))
            {
                continue;
            }

            filteredLines.Add(line);
        }

        var formatted = string.Join('\n', filteredLines);

        formatted = System.Text.RegularExpressions.Regex.Replace(
            formatted,
            @"\n{3,}",
            "\n\n");

        const int maxLength = 10000;
        if (formatted.Length > maxLength)
        {
            formatted = formatted[..maxLength] + "\n\n... (内容过长，已截断)";
        }

        return formatted.Trim();
    }

    /// <summary>
    /// 处理切换会话动作
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleSwitchSessionAsync(string? sessionId, string? chatKey)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(chatKey))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，切换失败", "error");
        }

        // 统一使用chatId作为key（去掉AppId前缀，和普通消息保持一致）
        var chatKeyParts = chatKey.Split(':');
        var actualChatKey = chatKeyParts.Length >= 3 ? chatKeyParts[2].ToLowerInvariant() : chatKey.ToLowerInvariant();

        var success = _feishuChannel.SwitchCurrentSession(actualChatKey, sessionId);
        if (success)
        {
            var workspacePath = _cliExecutor.GetSessionWorkspacePath(sessionId);
            var lastActiveTime = _feishuChannel.GetSessionLastActiveTime(sessionId);

            // 后台异步发送会话历史卡片
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("🔍 [会话历史] 开始获取会话 {SessionId} 历史消息", sessionId);

                    // 获取最近10条消息，按时间正序排列（最早在上，最新在下）
                    var messages = _chatSessionService.GetMessages(sessionId)
                        .OrderBy(m => m.CreatedAt)
                        .TakeLast(10)
                        .ToList();

                    _logger.LogInformation("🔍 [会话历史] 找到 {Count} 条历史消息", messages.Count);

                    var contentBuilder = new System.Text.StringBuilder();
                    contentBuilder.AppendLine($"## 📜 会话历史 `{sessionId[..8]}`");
                    contentBuilder.AppendLine($"⏱️ 最后活跃: {lastActiveTime:yyyy-MM-dd HH:mm}");
                    contentBuilder.AppendLine($"📂 工作目录: `{workspacePath}`");
                    contentBuilder.AppendLine();
                    contentBuilder.AppendLine("---");
                    contentBuilder.AppendLine();

                    if (messages.Count == 0)
                    {
                        contentBuilder.AppendLine("ℹ️ 该会话暂无历史消息");
                    }
                    else
                    {
                        foreach (var msg in messages)
                        {
                            var role = msg.Role == "user" ? "👤 用户" : "🤖 AI助手";
                            contentBuilder.AppendLine($"### {role} `{msg.CreatedAt:HH:mm}`");
                            contentBuilder.AppendLine(msg.Content);
                            contentBuilder.AppendLine();
                            contentBuilder.AppendLine("---");
                            contentBuilder.AppendLine();
                        }
                    }

                    _logger.LogInformation("🔍 [会话历史] 内容构建完成，长度: {Length}", contentBuilder.Length);

                    // 直接发送Markdown内容，系统会自动包装成卡片
                    _logger.LogInformation("🔍 [会话历史] 开始发送消息到聊天 {ChatId}", actualChatKey);
                    var messageId = await _feishuChannel.SendMessageAsync(actualChatKey, contentBuilder.ToString());
                    _logger.LogInformation("✅ [会话历史] 已发送会话 {SessionId} 历史到聊天 {ChatId}, MessageId={MessageId}", sessionId, actualChatKey, messageId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [会话历史] 发送会话历史失败，SessionId={SessionId}, ChatId={ChatId}", sessionId, actualChatKey);

                    // 尝试发送错误提示
                    try
                    {
                        await _feishuChannel.SendMessageAsync(actualChatKey, $"❌ 历史消息加载失败: {ex.Message}");
                    }
                    catch { }
                }
            });

            return _cardBuilder.BuildCardActionToastOnlyResponse(
                $"✅ 已切换到会话 {sessionId[..8]}...\n📂 当前工作目录: {workspacePath}\n📜 历史消息已发送到聊天窗口",
                "success");
        }

        return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 会话不存在，切换失败", "error");
    }

    /// <summary>
    /// 处理关闭会话动作
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleCloseSessionAsync(string? sessionId, string? chatKey)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(chatKey))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，关闭失败", "error");
        }

        // 统一使用chatId作为key（去掉AppId前缀，和普通消息保持一致）
        var chatKeyParts = chatKey.Split(':');
        var actualChatKey = chatKeyParts.Length >= 3 ? chatKeyParts[2].ToLowerInvariant() : chatKey.ToLowerInvariant();

        // 查询会话信息判断目录类型
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var session = await repo.GetByIdAsync(sessionId);

        if (session == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 会话不存在，关闭失败", "error");
        }

        // 自定义目录/项目目录：直接关闭，无需确认
        if (session.IsCustomWorkspace)
        {
            var success = _feishuChannel.CloseSession(actualChatKey, sessionId);
            if (success)
            {
                return _cardBuilder.BuildCardActionToastOnlyResponse(
                    $"🗑️ 已关闭会话 {sessionId[..8]}...\n✅ 自定义目录内容已保留",
                    "info");
            }
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 会话关闭失败", "error");
        }

        // 临时目录：需要二次确认
        lock (_pendingCloseSessions)
        {
            // 清理过期的待确认会话
            var expiredKeys = _pendingCloseSessions.Where(kv => kv.Value < DateTime.Now).Select(kv => kv.Key).ToList();
            foreach (var key in expiredKeys)
            {
                _pendingCloseSessions.Remove(key);
            }

            // 检查是否已在待确认列表中
            if (_pendingCloseSessions.TryGetValue(sessionId, out var expireTime) && expireTime > DateTime.Now)
            {
                // 确认有效期内，执行关闭
                _pendingCloseSessions.Remove(sessionId);
                var success = _feishuChannel.CloseSession(actualChatKey, sessionId);
                if (success)
                {
                    return _cardBuilder.BuildCardActionToastOnlyResponse(
                        $"🗑️ 已关闭会话 {sessionId[..8]}...\n⚠️ 临时目录内容已清理",
                        "info");
                }
                return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 会话关闭失败", "error");
            }
            else
            {
                // 第一次点击，加入待确认列表，有效期10秒
                _pendingCloseSessions[sessionId] = DateTime.Now.AddSeconds(10);
                return _cardBuilder.BuildCardActionToastOnlyResponse(
                    $"⚠️ 确认关闭临时会话 {sessionId[..8]} 吗？\n关闭后临时目录内容将被永久删除。\n请在10秒内再次点击确认。",
                    "warning");
            }
        }
    }

    /// <summary>
    /// 处理新建会话动作
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleCreateSessionAsync(string? chatKey, string? chatId)
    {
        if (string.IsNullOrEmpty(chatKey) || string.IsNullOrEmpty(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，创建失败", "error");
        }

        // 统一使用chatId作为key（去掉AppId前缀，和普通消息保持一致）
        var chatKeyParts = chatKey.Split(':');
        var actualChatKey = chatKeyParts.Length >= 3 ? chatKeyParts[2].ToLowerInvariant() : chatKey.ToLowerInvariant();
        var actualChatId = actualChatKey; // 现在chatKey就是chatId

        // 创建模拟消息对象用于创建新会话
        var mockMessage = new FeishuIncomingMessage
        {
            ChatId = actualChatId,
            SenderName = "用户"
        };
        _logger.LogInformation("🔍 [新建会话] 卡片回调ChatId={ChatId}, 实际使用ChatId={ActualChatId}", chatId, actualChatId);

        // 创建新会话（使用公开方法）
        var newSessionId = _feishuChannel.CreateNewSession(mockMessage, null);
        var workspacePath = _cliExecutor.GetSessionWorkspacePath(newSessionId);

        // 设置新会话为当前会话（使用统一的chatKey）
        _feishuChannel.SwitchCurrentSession(actualChatKey, newSessionId);

        return _cardBuilder.BuildCardActionToastOnlyResponse(
            $"✅ 已创建新会话 {newSessionId[..8]}...\n📂 工作目录: {workspacePath}\n已自动切换到新会话",
            "success");
    }

    /// <summary>
    /// 处理打开会话管理器动作
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleOpenSessionManagerAsync(string? chatId)
    {
        if (string.IsNullOrEmpty(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法打开会话管理", "error");
        }

        try
        {
            // 获取appId（需要从FeishuOptions获取，这里我们先构造chatKey）
            // 先获取FeishuOptions，通过_serviceProvider或者直接从_feishuChannel获取？
            // 我们可以通过反射获取_feishuChannel的_options字段
            // 直接使用chatId作为key（和普通消息保持一致，不需要AppId前缀）
            var chatKey = chatId.ToLowerInvariant();

            var sessions = _feishuChannel.GetChatSessions(chatKey);
            var currentSessionId = _feishuChannel.GetCurrentSession(chatKey);

            var elements = new List<object>();

            // 添加标题
            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"## 📋 会话管理\n当前聊天共有 **{sessions.Count}** 个会话"
                }
            });

            elements.Add(new { tag = "hr" });

            // 添加会话列表
            foreach (var sessionId in sessions.Take(10)) // 最多显示10个最近的会话
            {
                var workspacePath = _cliExecutor.GetSessionWorkspacePath(sessionId);
                var lastActiveTime = _feishuChannel.GetSessionLastActiveTime(sessionId);
                var isCurrent = sessionId == currentSessionId;

                var sessionInfo = $"{(isCurrent ? "✅ " : "")}**会话ID: {sessionId[..8]}...**\n📂 {workspacePath}\n⏱️ {lastActiveTime:yyyy-MM-dd HH:mm}";

                elements.Add(new
                {
                    tag = "div",
                    text = new { tag = "lark_md", content = sessionInfo },
                    actions = new[]
                    {
                        new
                        {
                            tag = "button",
                            text = new { tag = "plain_text", content = isCurrent ? "当前" : "切换" },
                            type = isCurrent ? "default" : "primary",
                            value = JsonSerializer.Serialize(new
                            {
                                action = "switch_session",
                                session_id = sessionId,
                                chat_key = chatKey
                            })
                        },
                        new
                        {
                            tag = "button",
                            text = new { tag = "plain_text", content = "关闭" },
                            type = "danger",
                            value = JsonSerializer.Serialize(new
                            {
                                action = "close_session",
                                session_id = sessionId,
                                chat_key = chatKey
                            })
                        }
                    }
                });
            }

            if (sessions.Count == 0)
            {
                elements.Add(new
                {
                    tag = "div",
                    text = new { tag = "plain_text", content = "暂无会话，请点击下方「新建会话」按钮创建会话。" }
                });
            }

            elements.Add(new { tag = "hr" });

            // 添加底部操作按钮
            elements.Add(new
            {
                tag = "button",
                text = new { tag = "plain_text", content = "➕ 新建会话" },
                type = "primary",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "create_session",
                            chat_key = chatKey
                        }
                    }
                }
            });

            elements.Add(new
            {
                tag = "button",
                text = new { tag = "plain_text", content = "🔙 返回帮助" },
                type = "default",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "back_to_list"
                        }
                    }
                }
            });

            // 构建卡片
            var card = new ElementsCardV2Dto
            {
                Header = new ElementsCardV2Dto.HeaderSuffix
                {
                    Template = "blue",
                    Title = new HeaderTitleElement { Content = "📋 会话管理" }
                },
                Config = new ElementsCardV2Dto.ConfigSuffix
                {
                    EnableForward = true,
                    UpdateMulti = true
                },
                Body = new ElementsCardV2Dto.BodySuffix
                {
                    Elements = elements.ToArray()
                }
            };

            return _cardBuilder.BuildCardActionResponseV2(card, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理打开会话管理失败");
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 会话管理功能暂时不可用，请稍后重试。", "error");
        }
    }
}
