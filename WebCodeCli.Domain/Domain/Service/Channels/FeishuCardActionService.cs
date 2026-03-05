using System.Text.Json;
using FeishuNetSdk.CallbackEvents;
using WebCodeCli.Domain.Domain.Model.Channels;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;

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
    private readonly ILogger<FeishuCardActionService> _logger;

    // 默认使用的 CLI 工具 ID
    private const string DefaultToolId = "claude-code";

    // 会话映射（从 FeishuChannelService 复制）
    private readonly Dictionary<string, string> _sessionMappings = new();

    public FeishuCardActionService(
        FeishuCommandService commandService,
        FeishuHelpCardBuilder cardBuilder,
        IFeishuCardKitClient cardKit,
        ICliExecutorService cliExecutor,
        IChatSessionService chatSessionService,
        ILogger<FeishuCardActionService> logger)
    {
        _commandService = commandService;
        _cardBuilder = cardBuilder;
        _cardKit = cardKit;
        _cliExecutor = cliExecutor;
        _chatSessionService = chatSessionService;
        _logger = logger;
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
}
