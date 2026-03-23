using System.Text.Json;
using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Im.Dtos;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Model.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Adapters;
using WebCodeCli.Domain.Repositories.Base.ChatSession;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.Json.Serialization;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书卡片回调处理服务
/// 处理卡片按钮点击的回调逻辑
/// </summary>
public class FeishuCardActionService
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    private readonly FeishuCommandService _commandService;
    private readonly FeishuHelpCardBuilder _cardBuilder;
    private readonly IFeishuCardKitClient _cardKit;
    private readonly ICliExecutorService _cliExecutor;
    private readonly IChatSessionService _chatSessionService;
    private readonly IFeishuChannelService _feishuChannel;
    private readonly ILogger<FeishuCardActionService> _logger;
    private readonly IServiceProvider _serviceProvider;

    // 默认回退工具 ID，最终会按当前会话和可用工具解析
    private const string FallbackToolId = "claude-code";
    private const int SessionDirectoryPageSize = 8;
    private const int SessionFilePreviewLineLimit = 80;
    private const int SessionFilePreviewCharacterLimit = 4000;
    private const int ProjectBranchPageSize = 12;

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
        string? inputValues = null,
        string? operatorUserId = null,
        string? appId = null)
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
                case "show_category":
                    return await HandleShowCategoryAsync(action.CategoryId, chatId);
                case "select_command":
                    return await HandleSelectCommandAsync(action.CommandId, chatId);
                case "back_to_list":
                    return await HandleBackToListAsync(chatId);
                case "execute_command":
                    return await HandleExecuteCommandAsync(formValueElement, action.Command, chatId, operatorUserId, inputValues, appId);
                case "switch_session":
                    return await HandleSwitchSessionAsync(action.SessionId, action.ChatKey, operatorUserId, appId);
                case "close_session":
                    return await HandleCloseSessionAsync(action.SessionId, action.ChatKey, operatorUserId);
                case "show_create_session_form":
                    return await HandleShowCreateSessionFormAsync(action.ChatKey, chatId, operatorUserId, action.ToolId);
                case "create_session":
                    return await HandleCreateSessionAsync(action.ChatKey, chatId, formValueElement, operatorUserId, action.CreateMode, action.WorkspacePath, action.ToolId, inputValues);
                case "browse_allowed_directory":
                    return await HandleBrowseAllowedDirectoryAsync(action.ChatKey, chatId, action.WorkspacePath, action.Page, operatorUserId, action.ToolId);
                case "copy_path_to_chat":
                    return await HandleCopyPathToChatAsync(action.ChatKey ?? chatId, action.CopyPath ?? action.WorkspacePath, operatorUserId);
                case "switch_tool":
                    return await HandleSwitchToolAsync(action.ToolId, action.ChatKey, operatorUserId);
                case "bind_web_user":
                    return await HandleBindWebUserAsync(formValueElement, chatId, operatorUserId, appId);
                case "open_session_manager":
                    return await HandleOpenSessionManagerAsync(action.ChatKey ?? chatId, operatorUserId);
                case "discover_external_cli_sessions":
                    return await HandleDiscoverExternalCliSessionsAsync(action.ChatKey ?? chatId, chatId, action.ToolId, action.Page, operatorUserId);
                case "import_external_cli_session":
                    return await HandleImportExternalCliSessionAsync(
                        action.ChatKey ?? chatId,
                        chatId,
                        action.ToolId,
                        action.CliThreadId,
                        action.Title,
                        action.WorkspacePath,
                        operatorUserId,
                        appId);
                case "open_project_manager":
                    return await HandleOpenProjectManagerAsync(action.ChatKey ?? chatId, operatorUserId);
                case "show_create_project_form":
                    return await HandleShowCreateProjectFormAsync(action.ChatKey ?? chatId);
                case "show_edit_project_form":
                    return await HandleShowEditProjectFormAsync(action.ChatKey ?? chatId, action.ProjectId, operatorUserId);
                case "create_project":
                    return await HandleCreateProjectAsync(action.ChatKey ?? chatId, formValueElement, operatorUserId);
                case "update_project":
                    return await HandleUpdateProjectAsync(action.ChatKey ?? chatId, action.ProjectId, formValueElement, operatorUserId);
                case "clone_project":
                    return await HandleCloneProjectAsync(action.ChatKey ?? chatId, action.ProjectId, operatorUserId, appId);
                case "pull_project":
                    return await HandlePullProjectAsync(action.ChatKey ?? chatId, action.ProjectId, operatorUserId, appId);
                case "show_project_branch_switcher":
                    return await HandleShowProjectBranchSwitcherAsync(action.ChatKey ?? chatId, action.ProjectId, action.Page, operatorUserId, appId);
                case "switch_project_branch":
                    return await HandleSwitchProjectBranchAsync(action.ChatKey ?? chatId, action.ProjectId, action.Branch, action.Page, operatorUserId, appId);
                case "delete_project":
                    return await HandleDeleteProjectAsync(action.ChatKey ?? chatId, action.ProjectId, operatorUserId, appId);
                case "fetch_project_branches":
                    return await HandleFetchProjectBranchesAsync(action.ChatKey ?? chatId, action.ProjectId, formValueElement, operatorUserId);
                case "create_session_from_project":
                    return await HandleCreateSessionFromProjectAsync(action.ChatKey ?? chatId, action.ProjectId, operatorUserId);
                case "browse_current_session_directory":
                    return await HandleBrowseCurrentSessionDirectoryAsync(action.ChatKey, chatId, operatorUserId);
                case "browse_session_directory":
                    return await HandleBrowseSessionDirectoryAsync(action.SessionId, action.ChatKey, action.DirectoryPath, action.Page, operatorUserId);
                case "preview_session_file":
                    return await HandlePreviewSessionFileAsync(action.SessionId, action.ChatKey, action.FilePath, action.DirectoryPath, action.Page, operatorUserId);
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
        var toolId = ResolveToolIdForChat(chatId);
        await _commandService.RefreshCommandsAsync(toolId);
        var categories = await _commandService.GetCategorizedCommandsAsync(toolId);
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
            var categories = await _commandService.GetCategorizedCommandsAsync(ResolveToolIdForChat(chatId));
            var card = _cardBuilder.BuildCommandListCardV2(categories);
            return _cardBuilder.BuildCardActionResponseV2(card, "📋 显示命令列表", "info");
        }

        // 特殊处理：会话管理命令直接返回会话管理卡片，不进入执行界面
        if (commandId == "feishusessions")
        {
            return await HandleOpenSessionManagerAsync(chatId, null);
        }

        if (commandId == "feishuprojects")
        {
            return await HandleOpenProjectManagerAsync(chatId, null);
        }

        var toolId = ResolveToolIdForChat(chatId);
        var command = await _commandService.GetCommandAsync(commandId, toolId);
        if (command == null)
        {
            var categories = await _commandService.GetCategorizedCommandsAsync(toolId);
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

        var categories = await _commandService.GetCategorizedCommandsAsync(ResolveToolIdForChat(chatId));
        var card = _cardBuilder.BuildCommandListCardV2(categories);
        _logger.LogInformation("📋 [FeishuHelp] 返回命令列表卡片（回调响应）");
        return _cardBuilder.BuildCardActionResponseV2(card, "", "info");
    }

    /// <summary>
    /// 处理执行命令
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleExecuteCommandAsync(
        JsonElement? formValue,
        string? commandFromAction,
        string? chatId,
        string? operatorUserId,
        string? inputValues = null,
        string? appId = null)
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

        var actualChatKey = NormalizeChatKey(chatId);
        var currentSessionId = _feishuChannel.GetCurrentSession(actualChatKey);
        if (string.IsNullOrWhiteSpace(currentSessionId))
        {
            _logger.LogInformation("⚠️ [FeishuHelp] 当前聊天没有活跃会话，跳转到新建会话表单: ChatId={ChatId}", chatId);
            var toolId = ResolveToolIdForChat(chatId);
            var response = await HandleShowCreateSessionFormAsync(actualChatKey, chatId, null, toolId);
            response.Toast = new CardActionTriggerResponseDto.ToastSuffix
            {
                Content = "⚠️ 当前没有活跃会话，请先新建会话并选择目录",
                Type = CardActionTriggerResponseDto.ToastSuffix.ToastType.Warning
            };
            return response;
        }

        if (IsHistoryCommand(commandInput))
        {
            var historyToast = _cardBuilder.BuildCardActionToastOnlyResponse("📜 正在读取当前 CLI 会话历史...", "info");

            _ = Task.Run(async () =>
            {
                try
                {
                    var username = ResolveFeishuUsername(chatId.ToLowerInvariant(), operatorUserId);
                    await SendExternalCliHistoryAsync(currentSessionId, actualChatKey, username, appId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [FeishuHelp] 读取当前 CLI 会话历史失败");
                }
            });

            return historyToast;
        }

        // 立即返回 toast 响应
        var toastResponse = _cardBuilder.BuildCardActionToastOnlyResponse("🚀 开始执行命令...", "info");

        // 在后台执行命令（不等待）
        _ = Task.Run(async () =>
        {
            try
            {
                var username = ResolveFeishuUsername(chatId.ToLowerInvariant(), operatorUserId);
                var effectiveOptions = await ResolveEffectiveOptionsAsync(username, appId);

                // 获取或创建会话
                var toolId = ResolveToolIdForChat(chatId);
                var sessionId = GetOrCreateSession(chatId, toolId);

                // 添加用户消息到会话
                _chatSessionService.AddMessage(sessionId, new Domain.Model.ChatMessage
                {
                    Role = "user",
                    Content = commandInput,
                    CliToolId = toolId,
                    CreatedAt = DateTime.Now
                });

                // 创建流式回复
                var handle = await _cardKit.CreateStreamingHandleAsync(
                    chatId,
                    null,
                    effectiveOptions.ThinkingMessage,
                    effectiveOptions.DefaultCardTitle,
                    optionsOverride: effectiveOptions);

                _logger.LogInformation(
                    "🔥 [FeishuHelp] 流式句柄已创建: CardId={CardId}",
                    handle.CardId);

                // 执行 CLI 工具并流式更新卡片
                await ExecuteCliAndStreamAsync(handle, sessionId, toolId, commandInput, chatId, effectiveOptions.ThinkingMessage);
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
    private string GetOrCreateSession(string chatId, string toolId)
    {
        var activeChatKey = NormalizeChatKey(chatId);
        var currentSessionId = _feishuChannel.GetCurrentSession(activeChatKey);
        if (!string.IsNullOrWhiteSpace(currentSessionId))
        {
            _logger.LogInformation(
                "Using active Feishu session: {SessionId} for chat: {ChatId}",
                currentSessionId,
                chatId);
            return currentSessionId;
        }

        var normalizedToolId = NormalizeToolId(toolId) ?? ResolveDefaultToolId();
        var chatKey = $"feishu:help:{chatId}:{normalizedToolId}";

        if (_sessionMappings.TryGetValue(chatKey, out var existingSessionId))
        {
            _logger.LogDebug("Using existing session: {SessionId} for chat: {ChatId}", existingSessionId, chatId);
            return existingSessionId;
        }

        var newSessionId = Guid.NewGuid().ToString();
        _sessionMappings[chatKey] = newSessionId;

        _logger.LogInformation(
            "Created new session: {SessionId} for chat: {ChatId}, ToolId={ToolId}",
            newSessionId,
            chatId,
            normalizedToolId);

        return newSessionId;
    }

    /// <summary>
    /// 处理分类选择 - 显示该分类下的命令按钮
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleShowCategoryAsync(string? categoryId, string? chatId)
    {
        if (string.IsNullOrEmpty(chatId))
        {
            _logger.LogWarning("❌ [FeishuHelp] 没有 chatId，无法显示分类命令");
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 缺少 chatId", "error");
        }

        var toolId = ResolveToolIdForChat(chatId);
        var categories = await _commandService.GetCategorizedCommandsAsync(toolId);

        if (string.IsNullOrEmpty(categoryId))
        {
            var card = _cardBuilder.BuildCommandListCardV2(categories);
            return _cardBuilder.BuildCardActionResponseV2(card, "📋 显示命令列表", "info");
        }

        var category = categories.FirstOrDefault(c => string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase));
        if (category == null)
        {
            var card = _cardBuilder.BuildCommandListCardV2(categories);
            return _cardBuilder.BuildCardActionResponseV2(card, "❌ 分类不存在", "warning");
        }

        var categoryCard = _cardBuilder.BuildCategoryCommandsCardV2(category);
        _logger.LogInformation("📋 [FeishuHelp] 返回分类命令卡片: {Category}", category.Name);
        return _cardBuilder.BuildCardActionResponseV2(categoryCard, "", "info");
    }

    /// <summary>
    /// 执行 CLI 工具并流式更新卡片（从 FeishuChannelService 复制）
    /// </summary>
    private async Task ExecuteCliAndStreamAsync(
        FeishuStreamingHandle handle,
        string sessionId,
        string toolId,
        string userPrompt,
        string chatId,
        string thinkingMessage)
    {
        var outputBuilder = new System.Text.StringBuilder();
        var assistantMessageBuilder = new System.Text.StringBuilder();
        var jsonlBuffer = new System.Text.StringBuilder();
        var resolvedToolId = NormalizeToolId(toolId) ?? ResolveDefaultToolId();
        var tool = _cliExecutor.GetTool(resolvedToolId);

        if (tool == null)
        {
            await handle.FinishAsync($"错误：未找到 CLI 工具 '{resolvedToolId}'，请在配置中添加该工具。");
            _logger.LogWarning("CLI tool not found: {ToolId}", resolvedToolId);
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
                        displayContent = thinkingMessage;
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
                    finalOutput = ExtractFallbackOutput(outputBuilder.ToString(), adapter!) ?? "无输出";
                }
            }
            else
            {
                finalOutput = FormatMarkdownOutput(outputBuilder.ToString());
            }

            try
            {
                await _feishuChannel.SendMessageAsync(chatId, "已完成");
            }
            catch (Exception notificationEx)
            {
                _logger.LogWarning(notificationEx, "发送完成通知失败: ChatId={ChatId}", chatId);
            }

            await handle.FinishAsync(finalOutput);

            _chatSessionService.AddMessage(sessionId, new Domain.Model.ChatMessage
            {
                Role = "assistant",
                Content = finalOutput,
                CliToolId = tool.Id,
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

    private string? ExtractFallbackOutput(string fullOutput, ICliToolAdapter adapter)
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

    private async Task<CardActionTriggerResponseDto> HandleBindWebUserAsync(JsonElement? formValue, string? chatId, string? operatorUserId, string? appId)
    {
        var webUsername = GetFormStringValue(formValue, "web_username")?.Trim();
        var webPassword = GetFormStringValue(formValue, "web_password")?.Trim();

        if (string.IsNullOrWhiteSpace(operatorUserId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 无法识别当前飞书用户，绑定失败", "error");
        }

        if (string.IsNullOrWhiteSpace(webUsername) || string.IsNullOrWhiteSpace(webPassword))
        {
            using var retryScope = _serviceProvider.CreateScope();
            var retryBindingService = retryScope.ServiceProvider.GetRequiredService<IFeishuUserBindingService>();
            var retryCard = _cardBuilder.BuildBindWebUserCardV2((await retryBindingService.GetBindableWebUsernamesAsync(appId)).ToArray());
            return _cardBuilder.BuildCardActionResponseV2(retryCard, "⚠️ 请输入用户名和密码", "warning");
        }

        using var scope = _serviceProvider.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var bindingService = scope.ServiceProvider.GetRequiredService<IFeishuUserBindingService>();

        if (!authService.ValidateUser(webUsername, webPassword))
        {
            var retryCard = _cardBuilder.BuildBindWebUserCardV2((await bindingService.GetBindableWebUsernamesAsync(appId)).ToArray());
            return _cardBuilder.BuildCardActionResponseV2(retryCard, "❌ 用户名或密码错误", "error");
        }

        var bindResult = await bindingService.BindAsync(operatorUserId, webUsername, appId);
        if (!bindResult.Success)
        {
            var retryCard = _cardBuilder.BuildBindWebUserCardV2((await bindingService.GetBindableWebUsernamesAsync(appId)).ToArray());
            return _cardBuilder.BuildCardActionResponseV2(retryCard, $"❌ 绑定失败：{bindResult.ErrorMessage}", "error");
        }

        if (!string.IsNullOrWhiteSpace(chatId))
        {
            var sessionManagerCard = await HandleOpenSessionManagerAsync(chatId, operatorUserId);
            sessionManagerCard.Toast = new CardActionTriggerResponseDto.ToastSuffix
            {
                Content = $"✅ 已绑定 Web 用户：{webUsername}",
                Type = CardActionTriggerResponseDto.ToastSuffix.ToastType.Success
            };
            return sessionManagerCard;
        }

        return _cardBuilder.BuildCardActionToastOnlyResponse($"✅ 已绑定 Web 用户：{webUsername}", "success");
    }

    /// <summary>
    /// 处理切换会话动作
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleSwitchSessionAsync(string? sessionId, string? chatKey, string? operatorUserId, string? appId)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(chatKey))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，切换失败", "error");
        }

        // 统一使用chatId作为key（去掉AppId前缀，和普通消息保持一致）
        var chatKeyParts = chatKey.Split(':');
        var actualChatKey = chatKeyParts.Length >= 3 ? chatKeyParts[2].ToLowerInvariant() : chatKey.ToLowerInvariant();

        var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
        var success = _feishuChannel.SwitchCurrentSession(actualChatKey, sessionId, username);
        if (success)
        {
            var workspacePath = GetSessionWorkspaceDisplay(sessionId);
            var lastActiveTime = _feishuChannel.GetSessionLastActiveTime(sessionId);
            using var detailScope = _serviceProvider.CreateScope();
            var detailRepo = detailScope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
            var sessionEntity = await detailRepo.GetByIdAsync(sessionId);
            var toolLabel = GetToolDisplayName(sessionEntity?.ToolId);

            // 后台异步发送会话历史卡片
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendExternalCliHistoryAsync(sessionId, actualChatKey, username, appId, lastActiveTime, workspacePath, toolLabel);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [会话历史] 发送会话历史失败，SessionId={SessionId}, ChatId={ChatId}", sessionId, actualChatKey);

                    // 尝试发送错误提示
                    try
                    {
                        await _feishuChannel.SendMessageAsync(actualChatKey, $"❌ 历史消息加载失败: {ex.Message}", username, appId);
                    }
                    catch { }
                }
            });

            return _cardBuilder.BuildCardActionToastOnlyResponse(
                $"✅ 已切换到会话 {sessionId[..8]}...\n🛠️ CLI 工具: {toolLabel}\n📂 当前工作目录: {workspacePath}\n📜 历史消息已发送到聊天窗口",
                "success");
        }

        return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 会话不存在，切换失败", "error");
    }

    private async Task SendExternalCliHistoryAsync(
        string sessionId,
        string chatId,
        string username,
        string? appId,
        DateTime? lastActiveTime = null,
        string? workspacePath = null,
        string? toolLabel = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var detailRepo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var historyService = scope.ServiceProvider.GetRequiredService<IExternalCliSessionHistoryService>();

        var sessionEntity = await detailRepo.GetByIdAsync(sessionId);
        if (sessionEntity == null)
        {
            await _feishuChannel.SendMessageAsync(chatId, $"❌ 会话 {sessionId[..Math.Min(8, sessionId.Length)]} 不存在，无法读取历史消息", username, appId);
            return;
        }

        var normalizedToolId = NormalizeToolId(sessionEntity.ToolId) ?? ResolveDefaultToolId();
        var cliThreadId = sessionEntity.CliThreadId?.Trim();
        if (string.IsNullOrWhiteSpace(cliThreadId))
        {
            await _feishuChannel.SendMessageAsync(chatId, "⚠️ 当前会话尚未绑定 CLI 原生会话 ID，暂时无法读取历史消息。请先在该会话中执行一次 CLI 对话。", username, appId);
            return;
        }

        _logger.LogInformation(
            "🔍 [会话历史] 开始获取外部 CLI 会话历史: SessionId={SessionId}, ToolId={ToolId}, CliThreadId={CliThreadId}",
            sessionId,
            normalizedToolId,
            cliThreadId);

        var messages = await historyService.GetRecentMessagesAsync(normalizedToolId, cliThreadId, maxCount: 10);
        var content = BuildExternalCliHistoryText(
            sessionId,
            toolLabel ?? GetToolDisplayName(sessionEntity.ToolId),
            workspacePath ?? GetSessionWorkspaceDisplay(sessionId),
            lastActiveTime ?? _feishuChannel.GetSessionLastActiveTime(sessionId),
            messages);

        var messageId = await _feishuChannel.SendMessageAsync(chatId, content, username, appId);
        _logger.LogInformation(
            "✅ [会话历史] 已发送外部 CLI 会话历史: SessionId={SessionId}, ChatId={ChatId}, MessageId={MessageId}, Count={Count}",
            sessionId,
            chatId,
            messageId,
            messages.Count);
    }

    private static string BuildExternalCliHistoryText(
        string sessionId,
        string toolLabel,
        string workspacePath,
        DateTime? lastActiveTime,
        IReadOnlyList<ExternalCliHistoryMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"当前 CLI 会话历史 {sessionId[..Math.Min(8, sessionId.Length)]}");
        builder.AppendLine($"CLI 工具: {toolLabel}");
        builder.AppendLine($"工作目录: {workspacePath}");
        if (lastActiveTime.HasValue)
        {
            builder.AppendLine($"最后活跃: {lastActiveTime:yyyy-MM-dd HH:mm}");
        }

        builder.AppendLine();

        if (messages.Count == 0)
        {
            builder.AppendLine("该 CLI 会话暂无可解析的历史消息。");
            return builder.ToString().TrimEnd();
        }

        foreach (var message in messages.TakeLast(10))
        {
            var roleLabel = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                ? "用户"
                : "助手";

            if (message.CreatedAt.HasValue)
            {
                builder.AppendLine($"[{roleLabel}] {message.CreatedAt:HH:mm}");
            }
            else
            {
                builder.AppendLine($"[{roleLabel}]");
            }

            builder.AppendLine(TrimHistoryContent(message.Content, 1200));
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string TrimHistoryContent(string? content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Replace("\r\n", "\n").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength] + "\n...";
    }

    private static bool IsHistoryCommand(string? commandInput)
    {
        if (string.IsNullOrWhiteSpace(commandInput))
        {
            return false;
        }

        var trimmed = commandInput.Trim();
        return string.Equals(trimmed, "/history", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("/history ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 处理旧卡片中的切换工具动作
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleSwitchToolAsync(string? toolId, string? chatKey, string? operatorUserId)
    {
        var normalizedChatKey = string.IsNullOrWhiteSpace(chatKey) ? chatKey : NormalizeChatKey(chatKey);
        var response = await HandleOpenSessionManagerAsync(normalizedChatKey, operatorUserId);
        response.Toast = new CardActionTriggerResponseDto.ToastSuffix
        {
            Content = "⚠️ 现有会话不支持切换 CLI 工具，请新建会话时选择工具",
            Type = CardActionTriggerResponseDto.ToastSuffix.ToastType.Warning
        };
        return response;
    }

    /// <summary>
    /// 处理关闭会话动作
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleCloseSessionAsync(string? sessionId, string? chatKey, string? operatorUserId)
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

        var hasExistingWorkspace = !string.IsNullOrWhiteSpace(session.WorkspacePath)
            && Directory.Exists(session.WorkspacePath);
        var requiresTempWorkspaceConfirmation = !session.IsCustomWorkspace && hasExistingWorkspace;

        // 自定义目录、项目目录、或已丢失工作区的历史坏会话：直接关闭，无需确认
        if (!requiresTempWorkspaceConfirmation)
        {
            var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
            var success = _feishuChannel.CloseSession(actualChatKey, sessionId, username);
            if (success)
            {
                var closeMessage = session.IsCustomWorkspace
                    ? $"🗑️ 已关闭会话 {sessionId[..8]}...\n✅ 自定义目录内容已保留"
                    : $"🗑️ 已关闭会话 {sessionId[..8]}...\nℹ️ 该会话没有可清理的临时工作区";
                return _cardBuilder.BuildCardActionToastOnlyResponse(
                    closeMessage,
                    "info");
            }
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 会话关闭失败", "error");
        }

        // 仍然存在的临时目录：需要二次确认
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
                var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
            var success = _feishuChannel.CloseSession(actualChatKey, sessionId, username);
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
    /// 显示新建会话表单
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleShowCreateSessionFormAsync(string? chatKey, string? chatId, string? operatorUserId, string? selectedToolId)
    {
        if (string.IsNullOrEmpty(chatKey) || string.IsNullOrEmpty(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法打开创建表单", "error");
        }

        var actualChatKey = NormalizeChatKey(chatKey);
        var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
        if (string.IsNullOrWhiteSpace(username))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 无法识别当前用户，请先发送一条普通消息后再创建会话", "error");
        }

        using var scope = _serviceProvider.CreateScope();
        var sessionDirectoryService = scope.ServiceProvider.GetRequiredService<ISessionDirectoryService>();
        var directories = await sessionDirectoryService.GetUserAccessibleDirectoriesAsync(username);
        var availableTools = _cliExecutor.GetAvailableTools();
        var effectiveToolId = NormalizeToolId(selectedToolId);
        if (string.IsNullOrWhiteSpace(effectiveToolId) || _cliExecutor.GetTool(effectiveToolId) == null)
        {
            effectiveToolId = ResolveToolIdForChat(actualChatKey, username);
        }

        _logger.LogInformation("[Feishu] 新建会话卡片加载可访问目录: User={User}, Count={Count}", username, directories.Count);
        var card = BuildCreateSessionFormCard(actualChatKey, directories, availableTools, effectiveToolId);
        return _cardBuilder.BuildCardActionResponseV2(card, $"请选择工作区和 CLI 工具（当前选择：{GetToolDisplayName(effectiveToolId)}）");
    }

    /// <summary>
    /// 处理新建会话动作
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleCreateSessionAsync(string? chatKey, string? chatId, JsonElement? formValue, string? operatorUserId, string? createModeFromAction, string? workspacePathFromAction, string? toolIdFromAction, string? inputValues)
    {
        if (string.IsNullOrEmpty(chatKey) || string.IsNullOrEmpty(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，创建失败", "error");
        }

        var actualChatKey = NormalizeChatKey(chatKey);
        var actualChatId = actualChatKey;
        var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
        if (string.IsNullOrWhiteSpace(username))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 无法识别当前用户，请先发送一条普通消息后再创建会话", "error");
        }

        var createMode = string.IsNullOrWhiteSpace(createModeFromAction)
            ? GetFormStringValue(formValue, "workspace_mode") ?? "default"
            : createModeFromAction;
        var customWorkspacePath = GetFormStringValue(formValue, "custom_workspace_path");
        var existingWorkspacePath = GetFormStringValue(formValue, "existing_workspace_path");
        var selectedToolId = NormalizeToolId(toolIdFromAction)
            ?? NormalizeToolId(GetFormStringValue(formValue, "tool_id") ?? string.Empty)
            ?? NormalizeToolId(ResolveDefaultToolId())
            ?? FallbackToolId;

        if (_cliExecutor.GetTool(selectedToolId) == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse($"❌ 工具不存在或未启用：{selectedToolId}", "error");
        }

        _logger.LogInformation("[Feishu] 创建会话提交: Mode={Mode}, ToolId={ToolId}, WorkspacePathFromAction={WorkspacePathFromAction}, InputValues={InputValues}, FormValue={FormValue}",
            createMode,
            selectedToolId,
            workspacePathFromAction,
            inputValues,
            formValue?.ToString());

        string? selectedWorkspacePath = null;
        switch ((createMode ?? "default").ToLowerInvariant())
        {
            case "custom":
                selectedWorkspacePath = !string.IsNullOrWhiteSpace(customWorkspacePath)
                    ? customWorkspacePath.Trim()
                    : (!string.IsNullOrWhiteSpace(inputValues) ? inputValues.Trim() : null);
                if (string.IsNullOrWhiteSpace(selectedWorkspacePath))
                {
                    return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请输入自定义工作区路径，然后按回车创建", "error");
                }
                break;
            case "existing":
                selectedWorkspacePath = !string.IsNullOrWhiteSpace(workspacePathFromAction)
                    ? workspacePathFromAction.Trim()
                    : existingWorkspacePath?.Trim();
                if (string.IsNullOrWhiteSpace(selectedWorkspacePath))
                {
                    return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请选择一个已有目录", "error");
                }
                break;
        }

        var mockMessage = new FeishuIncomingMessage
        {
            ChatId = actualChatId,
            SenderName = username
        };

        try
        {
            var newSessionId = _feishuChannel.CreateNewSession(mockMessage, selectedWorkspacePath, selectedToolId);
            var workspacePath = string.IsNullOrWhiteSpace(selectedWorkspacePath)
                ? _cliExecutor.GetSessionWorkspacePath(newSessionId)
                : selectedWorkspacePath;
            _feishuChannel.SwitchCurrentSession(actualChatKey, newSessionId, username);
            var toolLabel = GetToolDisplayName(selectedToolId);

            return _cardBuilder.BuildCardActionToastOnlyResponse(
                $"✅ 已创建新会话 {newSessionId[..8]}...\n🛠️ CLI 工具: {toolLabel}\n📂 工作目录: {workspacePath}\n已自动切换到新会话",
                "success");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [新建会话] 创建飞书会话失败, ChatId={ChatId}, User={User}", actualChatId, username);
            return _cardBuilder.BuildCardActionToastOnlyResponse($"❌ 创建失败: {ex.Message}", "error");
        }
    }

    private async Task<CardActionTriggerResponseDto> HandleBrowseAllowedDirectoryAsync(
        string? chatKey,
        string? chatId,
        string? workspacePath,
        int? page,
        string? operatorUserId,
        string? selectedToolId)
    {
        var actualChatKey = !string.IsNullOrWhiteSpace(chatKey)
            ? NormalizeChatKey(chatKey)
            : (!string.IsNullOrWhiteSpace(chatId) ? NormalizeChatKey(chatId) : string.Empty);

        if (string.IsNullOrWhiteSpace(actualChatKey))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法浏览白名单目录", "error");
        }

        var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
        if (string.IsNullOrWhiteSpace(username))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再浏览白名单目录", "error");
        }

        using var scope = _serviceProvider.CreateScope();
        var sessionDirectoryService = scope.ServiceProvider.GetRequiredService<ISessionDirectoryService>();

        try
        {
            var browseResult = await sessionDirectoryService.BrowseAllowedDirectoriesAsync(workspacePath, username);
            var effectiveToolId = NormalizeToolId(selectedToolId) ?? ResolveToolIdForChat(actualChatKey, username);
            var card = BuildAllowedDirectoryCard(actualChatKey, effectiveToolId, browseResult, Math.Max(page ?? 0, 0));
            return _cardBuilder.BuildCardActionResponseV2(card, string.Empty);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "[Feishu] 浏览白名单目录被拒绝: Path={Path}", workspacePath);
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 无法访问该白名单目录", "error");
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Feishu] 白名单目录不存在: Path={Path}", workspacePath);
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 白名单目录不存在或已被删除", "error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Feishu] 浏览白名单目录失败: Path={Path}", workspacePath);
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 白名单目录浏览失败，请稍后重试", "error");
        }
    }

    private async Task<CardActionTriggerResponseDto> HandleCopyPathToChatAsync(string? chatKey, string? path, string? operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(chatKey))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 缺少聊天标识，无法发送路径", "error");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 路径为空，无法复制", "error");
        }

        var actualChatKey = NormalizeChatKey(chatKey);

        try
        {
            var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
            var prefix = string.IsNullOrWhiteSpace(username) ? "可复制路径" : $"{username} 的可复制路径";
            await _feishuChannel.SendMessageAsync(actualChatKey, $"{prefix}:\n{path}", username);
            return _cardBuilder.BuildCardActionToastOnlyResponse("✅ 路径已发送到聊天，可长按复制", "success");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Feishu] 发送可复制路径失败: ChatKey={ChatKey}", actualChatKey);
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 路径发送失败，请稍后重试", "error");
        }
    }

    private string NormalizeChatKey(string chatKey)
    {
        var chatKeyParts = chatKey.Split(':');
        return chatKeyParts.Length >= 3 ? chatKeyParts[2].ToLowerInvariant() : chatKey.ToLowerInvariant();
    }

    private string ResolveToolIdForChat(string? chatId, string? username = null)
    {
        var normalizedChatKey = string.IsNullOrWhiteSpace(chatId) ? string.Empty : NormalizeChatKey(chatId);
        if (!string.IsNullOrWhiteSpace(normalizedChatKey))
        {
            return _feishuChannel.ResolveToolId(normalizedChatKey, username);
        }

        return ResolveDefaultToolId();
    }

    private string ResolveDefaultToolId()
    {
        foreach (var candidate in new[] { FallbackToolId, "codex", "opencode" })
        {
            var normalized = NormalizeToolId(candidate);
            if (!string.IsNullOrWhiteSpace(normalized) && _cliExecutor.GetTool(normalized) != null)
            {
                return normalized;
            }
        }

        return _cliExecutor.GetAvailableTools().FirstOrDefault()?.Id ?? FallbackToolId;
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

    private string? ResolveFeishuUsername(string chatKey, string? operatorUserId)
    {
        using var scope = _serviceProvider.CreateScope();
        var bindingService = scope.ServiceProvider.GetRequiredService<IFeishuUserBindingService>();

        if (!string.IsNullOrWhiteSpace(operatorUserId))
        {
            var boundWebUsername = bindingService.GetBoundWebUsernameAsync(operatorUserId).GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(boundWebUsername))
            {
                return boundWebUsername;
            }
        }

        return _feishuChannel.GetSessionUsername(chatKey);
    }

    private async Task<FeishuOptions> ResolveEffectiveOptionsAsync(string? username, string? appId = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var userFeishuBotConfigService = scope.ServiceProvider.GetRequiredService<IUserFeishuBotConfigService>();

        if (!string.IsNullOrWhiteSpace(appId))
        {
            var appOptions = await userFeishuBotConfigService.GetEffectiveOptionsByAppIdAsync(appId);
            if (appOptions != null)
            {
                return appOptions;
            }
        }

        return await userFeishuBotConfigService.GetEffectiveOptionsAsync(username);
    }

    private static string? GetFormStringValue(JsonElement? formValue, string key)
    {
        if (formValue == null || !formValue.Value.TryGetProperty(key, out var valueElement))
        {
            return null;
        }

        return valueElement.ValueKind switch
        {
            JsonValueKind.String => valueElement.GetString(),
            JsonValueKind.Object when valueElement.TryGetProperty("value", out var nestedValue) && nestedValue.ValueKind == JsonValueKind.String => nestedValue.GetString(),
            _ => valueElement.ToString()
        };
    }

    private CreateProjectRequest BuildProjectRequestFromForm(JsonElement? formValue)
    {
        var authType = NormalizeProjectAuthType(GetFormStringValue(formValue, "project_auth_type"));
        var request = new CreateProjectRequest
        {
            Name = GetFormStringValue(formValue, "project_name")?.Trim() ?? string.Empty,
            GitUrl = GetFormStringValue(formValue, "project_git_url")?.Trim() ?? string.Empty,
            AuthType = authType,
            Branch = GetFormStringValue(formValue, "project_branch")?.Trim() ?? string.Empty
        };

        if (authType == "https")
        {
            request.HttpsUsername = GetFormStringValue(formValue, "project_https_username")?.Trim();
            request.HttpsToken = NormalizeOptionalSecret(GetFormStringValue(formValue, "project_https_token"));
        }

        return request;
    }

    private UpdateProjectRequest BuildUpdateProjectRequestFromForm(JsonElement? formValue)
    {
        var authType = NormalizeProjectAuthType(GetFormStringValue(formValue, "project_auth_type"));
        var request = new UpdateProjectRequest
        {
            Name = GetFormStringValue(formValue, "project_name")?.Trim(),
            GitUrl = GetFormStringValue(formValue, "project_git_url")?.Trim(),
            AuthType = authType,
            Branch = GetFormStringValue(formValue, "project_branch")?.Trim() ?? string.Empty
        };

        if (authType == "https")
        {
            request.HttpsUsername = GetFormStringValue(formValue, "project_https_username")?.Trim();
            request.HttpsToken = NormalizeOptionalSecret(GetFormStringValue(formValue, "project_https_token"));
        }
        else
        {
            request.HttpsUsername = string.Empty;
            request.HttpsToken = string.Empty;
        }

        return request;
    }

    private static string NormalizeProjectAuthType(string? authType)
    {
        return (authType ?? "https").Trim().ToLowerInvariant() switch
        {
            "" => "https",
            "http" => "https",
            "basic" => "https",
            "token" => "https",
            "https" => "https",
            "ssh" => "ssh",
            _ => "none"
        };
    }

    private static string? NormalizeOptionalSecret(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private ProjectServiceScopeContext? CreateProjectScopeContext(string chatKey, string? operatorUserId)
    {
        var username = ResolveFeishuUsername(chatKey, operatorUserId);
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var scope = _serviceProvider.CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextService>();
        userContext.SetCurrentUsername(username);
        var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();
        return new ProjectServiceScopeContext(scope, projectService, username);
    }

    private static string GetProjectStatusLabel(ProjectInfo project)
    {
        return project.Status switch
        {
            "ready" => "✅",
            "cloning" => "🔄",
            "error" => "❌",
            _ => "⏳"
        };
    }

    private ElementsCardV2Dto BuildCreateSessionFormCard(string chatKey, List<object> directories, List<CliToolConfig> availableTools, string? selectedToolId)
    {
        var effectiveToolId = NormalizeToolId(selectedToolId) ?? ResolveDefaultToolId();
        var ownedDirectories = directories
            .Where(directory => GetDirectoryType(directory) == "owned")
            .Take(12)
            .ToList();

        var sharedDirectories = directories
            .Where(directory => GetDirectoryType(directory) == "authorized")
            .Take(12)
            .ToList();

        var elements = new List<object>
        {
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"## 🆕 新建会话\n先选择 **CLI 工具**，再选择 **默认目录**、**已有目录/项目**、**浏览白名单目录** 或 **自定义路径**。\n当前选择：**{GetToolDisplayName(effectiveToolId)}**"
                }
            },
            new { tag = "hr" },
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = "### 0️⃣ 选择 CLI 工具\n会话创建后工具固定，如需更换请新建会话。"
                }
            }
        };

        foreach (var availableTool in availableTools)
        {
            var normalizedToolId = NormalizeToolId(availableTool.Id);
            var isSelected = string.Equals(normalizedToolId, effectiveToolId, StringComparison.OrdinalIgnoreCase);
            elements.Add(new
            {
                tag = "button",
                text = new
                {
                    tag = "plain_text",
                    content = isSelected ? $"已选: {GetToolDisplayName(availableTool.Id)}" : $"选择 {GetToolDisplayName(availableTool.Id)}"
                },
                type = isSelected ? "default" : "primary",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "show_create_session_form",
                            chat_key = chatKey,
                            tool_id = availableTool.Id
                        }
                    }
                }
            });
        }

        if (availableTools.Count == 0)
        {
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "plain_text", content = "当前没有可用的 CLI 工具，无法创建会话。" }
            });
        }

        elements.AddRange(new object[]
        {
            new { tag = "hr" },
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = "### 1️⃣ 使用默认目录\n系统自动创建临时工作目录。"
                }
            },
            new
            {
                tag = "button",
                text = new { tag = "plain_text", content = "使用默认目录创建" },
                type = "primary",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "create_session",
                            create_mode = "default",
                            chat_key = chatKey,
                            tool_id = effectiveToolId
                        }
                    }
                }
            },
            new { tag = "hr" },
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = "### 2️⃣ 选择已有目录 / 项目"
                }
            }
        });

        if (ownedDirectories.Count > 0)
        {
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "lark_md", content = "**我的目录**" }
            });

            foreach (var directory in ownedDirectories)
            {
                elements.AddRange(BuildDirectoryCardElements(directory, chatKey, effectiveToolId));
            }
        }

        if (sharedDirectories.Count > 0)
        {
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "lark_md", content = "**共享给我的目录**" }
            });

            foreach (var directory in sharedDirectories)
            {
                elements.AddRange(BuildDirectoryCardElements(directory, chatKey, effectiveToolId));
            }
        }

        if (ownedDirectories.Count == 0 && sharedDirectories.Count == 0)
        {
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "plain_text", content = "暂无可用目录" }
            });
        }

        elements.Add(new { tag = "hr" });
        elements.Add(new
        {
            tag = "div",
            text = new
            {
                tag = "lark_md",
                content = "### 3️⃣ 浏览白名单目录\n优先显示当前用户配置的白名单目录；未单独配置时回退到 `Workspace:AllowedRoots` 全局目录，可浏览文件并复制路径。"
            }
        });
        elements.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "浏览白名单目录" },
            type = "default",
            behaviors = new[]
            {
                new
                {
                    type = "callback",
                    value = new
                    {
                        action = "browse_allowed_directory",
                        chat_key = chatKey,
                        tool_id = effectiveToolId
                    }
                }
            }
        });
        elements.Add(new { tag = "hr" });
        elements.Add(new
        {
            tag = "div",
            text = new
            {
                tag = "lark_md",
                content = "### 4️⃣ 自定义路径\n在下方输入框输入绝对路径，按回车直接创建。"
            }
        });
        elements.Add(new
        {
            tag = "input",
            input_type = "text",
            name = "custom_workspace_path",
            placeholder = new { tag = "plain_text", content = "例如: D:\\VSWorkshop\\testss" },
            behaviors = new[]
            {
                new
                {
                    type = "callback",
                    value = new
                    {
                        action = "create_session",
                        create_mode = "custom",
                        chat_key = chatKey,
                        tool_id = effectiveToolId
                    }
                }
            }
        });
        elements.Add(new { tag = "hr" });
        elements.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "返回会话管理" },
            type = "default",
            behaviors = new[]
            {
                new
                {
                    type = "callback",
                    value = new
                    {
                        action = "open_session_manager"
                    }
                }
            }
        });

        return new ElementsCardV2Dto
        {
            Header = new ElementsCardV2Dto.HeaderSuffix
            {
                Template = "blue",
                Title = new HeaderTitleElement { Content = "🆕 新建会话" }
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
    }

    private IEnumerable<object> BuildDirectoryCardElements(object directory, string chatKey, string toolId)
    {
        var node = JsonSerializer.SerializeToNode(directory) as JsonObject;
        if (node == null)
        {
            return Array.Empty<object>();
        }

        var path = node["DirectoryPath"]?.GetValue<string>() ?? node["directoryPath"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<object>();
        }

        var alias = node["Alias"]?.GetValue<string>()
            ?? node["alias"]?.GetValue<string>()
            ?? Path.GetFileName(path);
        var permission = node["Permission"]?.GetValue<string>() ?? node["permission"]?.GetValue<string>() ?? "owner";
        var directoryType = node["DirectoryType"]?.GetValue<string>()
            ?? node["directoryType"]?.GetValue<string>()
            ?? "workspace";

        return new object[]
        {
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"**{alias}**  `[type:{directoryType} permission:{permission}]`\n`{path}`"
                }
            },
            new
            {
                tag = "button",
                text = new { tag = "plain_text", content = "复制路径" },
                type = "default",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "copy_path_to_chat",
                            chat_key = chatKey,
                            copy_path = path
                        }
                    }
                }
            },
            new
            {
                tag = "button",
                text = new { tag = "plain_text", content = "使用该目录创建" },
                type = "primary",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "create_session",
                            create_mode = "existing",
                            workspace_path = path,
                            chat_key = chatKey,
                            tool_id = toolId
                        }
                    }
                }
            }
        };
    }

    private string GetDirectoryType(object directory)
    {
        var node = JsonSerializer.SerializeToNode(directory) as JsonObject;
        return node?["Type"]?.GetValue<string>()
            ?? node?["type"]?.GetValue<string>()
            ?? string.Empty;
    }

    private string GetSessionWorkspaceDisplay(string sessionId)
    {
        try
        {
            return _cliExecutor.GetSessionWorkspacePath(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Feishu] 从 CLI 缓存获取会话工作区失败，尝试仓储回退: {SessionId}", sessionId);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
                var sessionEntity = repo.GetByIdAsync(sessionId).GetAwaiter().GetResult();
                var workspacePath = sessionEntity?.WorkspacePath;

                if (!string.IsNullOrWhiteSpace(workspacePath) && Directory.Exists(workspacePath))
                {
                    return workspacePath;
                }

                _logger.LogWarning("[Feishu] 会话工作区仓储回退未命中或目录不存在: {SessionId}, WorkspacePath={WorkspacePath}", sessionId, workspacePath);
            }
            catch (Exception repoEx)
            {
                _logger.LogWarning(repoEx, "[Feishu] 从会话仓储回退工作区失败: {SessionId}", sessionId);
            }

            return "(工作区未初始化或已失效)";
        }
    }

    /// <summary>
    /// 处理打开会话管理器动作
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleOpenSessionManagerAsync(string? chatId, string? operatorUserId)
    {
        if (string.IsNullOrEmpty(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法打开会话管理", "error");
        }

        try
        {
            var chatKey = chatId.ToLowerInvariant();
            var username = ResolveFeishuUsername(chatKey, operatorUserId);
            if (string.IsNullOrWhiteSpace(username))
            {
                return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再管理会话", "error");
            }

            var sessionEntities = await GetChatSessionEntitiesAsync(chatKey, username);
            var sessions = sessionEntities.Select(s => s.SessionId).ToList();
            var currentSessionId = _feishuChannel.GetCurrentSession(chatKey, username);

            var elements = new List<object>();

            // 添加标题
            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"## 📋 会话管理\n当前聊天共有 **{sessions.Count}** 个会话\n🛠️ 每个会话的 CLI 工具在创建时确定，如需更换请新建会话。"
                }
            });

            elements.Add(new { tag = "hr" });

            if (!string.IsNullOrWhiteSpace(currentSessionId))
            {
                elements.Add(new
                {
                    tag = "button",
                    text = new { tag = "plain_text", content = "📂 浏览当前会话目录" },
                    type = "primary",
                    behaviors = new[]
                    {
                        new
                        {
                            type = "callback",
                            value = new
                            {
                                action = "browse_current_session_directory",
                                chat_key = chatKey
                            }
                        }
                    }
                });
                elements.Add(new { tag = "hr" });
            }

            // 添加会话列表
            foreach (var session in sessionEntities.Take(10)) // 最多显示10个最近的会话
            {
                var sessionId = session.SessionId;
                var workspacePath = GetSessionWorkspaceDisplay(sessionId);
                var isCurrent = sessionId == currentSessionId;
                var toolLabel = GetToolDisplayName(session.ToolId);

                var sessionInfo = $"{(isCurrent ? "✅ " : "")}**会话ID: {sessionId[..8]}...**\n🛠️ {toolLabel}\n📂 {workspacePath}\n⏱️ {session.UpdatedAt:yyyy-MM-dd HH:mm}";

                elements.Add(new
                {
                    tag = "div",
                    text = new { tag = "lark_md", content = sessionInfo }
                });

                elements.Add(new
                {
                    tag = "button",
                    text = new { tag = "plain_text", content = isCurrent ? "当前" : "切换" },
                    type = isCurrent ? "default" : "primary",
                    behaviors = new[]
                    {
                        new
                        {
                            type = "callback",
                            value = new
                            {
                                action = "switch_session",
                                session_id = sessionId,
                                chat_key = chatKey
                            }
                        }
                    }
                });

                elements.Add(new
                {
                    tag = "button",
                    text = new { tag = "plain_text", content = "关闭" },
                    type = "danger",
                    behaviors = new[]
                    {
                        new
                        {
                            type = "callback",
                            value = new
                            {
                                action = "close_session",
                                session_id = sessionId,
                                chat_key = chatKey
                            }
                        }
                    }
                });

                elements.Add(new { tag = "hr" });
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

            elements.Add(new
            {
                tag = "button",
                text = new { tag = "plain_text", content = "📁 项目管理" },
                type = "default",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "open_project_manager",
                            chat_key = chatKey
                        }
                    }
                }
            });

            elements.Add(new
            {
                tag = "button",
                text = new { tag = "plain_text", content = "📥 导入本地会话" },
                type = "default",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "discover_external_cli_sessions",
                            chat_key = chatKey
                        }
                    }
                }
            });

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
                            action = "show_create_session_form",
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

    private async Task<CardActionTriggerResponseDto> HandleDiscoverExternalCliSessionsAsync(
        string? chatKey,
        string? chatId,
        string? toolId,
        int? page,
        string? operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(chatKey) && string.IsNullOrWhiteSpace(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法发现本地会话", "error");
        }

        try
        {
            var actualChatKey = NormalizeChatKey(string.IsNullOrWhiteSpace(chatKey) ? chatId! : chatKey!);
            var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
            if (string.IsNullOrWhiteSpace(username))
            {
                return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再导入本地会话", "error");
            }

            using var scope = _serviceProvider.CreateScope();
            var externalService = scope.ServiceProvider.GetRequiredService<IExternalCliSessionService>();

            var normalizedToolId = string.IsNullOrWhiteSpace(toolId) ? null : NormalizeToolId(toolId);
            // 留出足够窗口，避免外部 CLI 会话总数增长后再次被硬截断。
            const int discoverMaxCount = 1000;
            const int pageSize = 10;
            var discovered = await externalService.DiscoverAsync(username, normalizedToolId, maxCount: discoverMaxCount);
            var totalPages = Math.Max(1, (int)Math.Ceiling(discovered.Count / (double)pageSize));
            var safePageIndex = Math.Clamp(page ?? 0, 0, totalPages - 1);
            var pageItems = discovered
                .Skip(safePageIndex * pageSize)
                .Take(pageSize)
                .ToList();

            var elements = new List<object>();

            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"## 📥 导入本地 CLI 会话\n当前找到 **{discovered.Count}** 个可导入会话。\n当前第 **{safePageIndex + 1}/{totalPages}** 页，每页 **{pageSize}** 条。\n导入后会将该会话绑定到当前聊天，并切换为活跃会话。"
                }
            });

            elements.Add(new { tag = "hr" });

            // 工具筛选按钮
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "plain_text", content = "筛选工具：" }
            });

            foreach (var (label, value) in new[]
                     {
                         ("全部", (string?)null),
                         ("OpenCode", "opencode"),
                         ("Codex", "codex"),
                         ("Claude Code", "claude-code")
                     })
            {
                var isSelected = string.IsNullOrWhiteSpace(value)
                    ? string.IsNullOrWhiteSpace(normalizedToolId)
                    : string.Equals(value, normalizedToolId, StringComparison.OrdinalIgnoreCase);
                elements.Add(new
                {
                    tag = "button",
                    text = new { tag = "plain_text", content = label },
                    type = isSelected ? "primary" : "default",
                    behaviors = new[]
                    {
                        new
                        {
                            type = "callback",
                            value = new
                            {
                                action = "discover_external_cli_sessions",
                                chat_key = actualChatKey,
                                tool_id = value,
                                page = 0
                            }
                        }
                    }
                });
            }

            elements.Add(new { tag = "hr" });

            var currentSessionId = _feishuChannel.GetCurrentSession(actualChatKey, username);
            if (discovered.Count == 0)
            {
                elements.Add(new
                {
                    tag = "div",
                    text = new { tag = "plain_text", content = "未发现可导入的本地会话。请确认：\n1) 该工具已在本机运行并产生会话记录\n2) 会话工作区在允许目录内" }
                });
            }
            else
            {
                foreach (var item in pageItems)
                {
                    var toolLabel = GetToolDisplayName(item.ToolId);
                    var updatedText = item.UpdatedAt.HasValue ? item.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "-";
                    var title = string.IsNullOrWhiteSpace(item.Title) ? item.CliThreadId : item.Title!;
                    var info = $"**[{toolLabel}] {title}**\n📂 {item.WorkspacePath}\n⏱️ {updatedText}";

                    elements.Add(new
                    {
                        tag = "div",
                        text = new { tag = "lark_md", content = info }
                    });

                    if (item.AlreadyImported && !string.IsNullOrWhiteSpace(item.ImportedSessionId))
                    {
                        var isCurrent = string.Equals(item.ImportedSessionId, currentSessionId, StringComparison.OrdinalIgnoreCase);
                        elements.Add(new
                        {
                            tag = "button",
                            text = new { tag = "plain_text", content = isCurrent ? "当前" : "切换到该会话" },
                            type = isCurrent ? "default" : "primary",
                            behaviors = new[]
                            {
                                new
                                {
                                    type = "callback",
                                    value = new
                                    {
                                        action = "switch_session",
                                        session_id = item.ImportedSessionId,
                                        chat_key = actualChatKey
                                    }
                                }
                            }
                        });
                    }
                    else
                    {
                        elements.Add(new
                        {
                            tag = "button",
                            text = new { tag = "plain_text", content = "导入并切换" },
                            type = "primary",
                            behaviors = new[]
                            {
                                new
                                {
                                    type = "callback",
                                    value = new
                                    {
                                        action = "import_external_cli_session",
                                        chat_key = actualChatKey,
                                        tool_id = item.ToolId,
                                        cli_thread_id = item.CliThreadId,
                                        title = item.Title,
                                        workspace_path = item.WorkspacePath
                                    }
                                }
                            }
                        });
                    }

                    elements.Add(new { tag = "hr" });
                }
            }

            if (discovered.Count > 0 && totalPages > 1)
            {
                elements.Add(new
                {
                    tag = "div",
                    text = new
                    {
                        tag = "plain_text",
                        content = $"分页：第 {safePageIndex + 1}/{totalPages} 页"
                    }
                });

                if (safePageIndex > 0)
                {
                    elements.Add(new
                    {
                        tag = "button",
                        text = new { tag = "plain_text", content = "⬅️ 上一页" },
                        type = "default",
                        behaviors = new[]
                        {
                            new
                            {
                                type = "callback",
                                value = new
                                {
                                    action = "discover_external_cli_sessions",
                                    chat_key = actualChatKey,
                                    tool_id = normalizedToolId,
                                    page = safePageIndex - 1
                                }
                            }
                        }
                    });
                }

                if (safePageIndex + 1 < totalPages)
                {
                    elements.Add(new
                    {
                        tag = "button",
                        text = new { tag = "plain_text", content = "➡️ 下一页" },
                        type = "default",
                        behaviors = new[]
                        {
                            new
                            {
                                type = "callback",
                                value = new
                                {
                                    action = "discover_external_cli_sessions",
                                    chat_key = actualChatKey,
                                    tool_id = normalizedToolId,
                                    page = safePageIndex + 1
                                }
                            }
                        }
                    });
                }
            }

            elements.Add(new
            {
                tag = "button",
                text = new { tag = "plain_text", content = "🔙 返回会话管理" },
                type = "default",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "open_session_manager",
                            chat_key = actualChatKey
                        }
                    }
                }
            });

            var card = new ElementsCardV2Dto
            {
                Header = new ElementsCardV2Dto.HeaderSuffix
                {
                    Template = "indigo",
                    Title = new HeaderTitleElement { Content = "📥 导入本地会话" }
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

            return _cardBuilder.BuildCardActionResponseV2(card, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理发现外部 CLI 会话失败");
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 发现本地会话失败，请稍后重试。", "error");
        }
    }

    private async Task<CardActionTriggerResponseDto> HandleImportExternalCliSessionAsync(
        string? chatKey,
        string? chatId,
        string? toolId,
        string? cliThreadId,
        string? title,
        string? workspacePath,
        string? operatorUserId,
        string? appId)
    {
        if (string.IsNullOrWhiteSpace(chatKey) && string.IsNullOrWhiteSpace(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，导入失败", "error");
        }

        var actualChatKey = NormalizeChatKey(string.IsNullOrWhiteSpace(chatKey) ? chatId! : chatKey!);
        var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
        if (string.IsNullOrWhiteSpace(username))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再导入本地会话", "error");
        }

        if (string.IsNullOrWhiteSpace(toolId) || string.IsNullOrWhiteSpace(cliThreadId) || string.IsNullOrWhiteSpace(workspacePath))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数不完整，导入失败", "error");
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var externalService = scope.ServiceProvider.GetRequiredService<IExternalCliSessionService>();

            var request = new ImportExternalCliSessionRequest
            {
                ToolId = toolId,
                CliThreadId = cliThreadId,
                Title = title,
                WorkspacePath = workspacePath
            };

            var result = await externalService.ImportAsync(username, request, feishuChatKey: actualChatKey);
            if (!result.Success)
            {
                return _cardBuilder.BuildCardActionToastOnlyResponse($"❌ 导入失败: {result.ErrorMessage}", "error");
            }

            // 发送普通文本提示（卡片 toast 不会提醒）
            try
            {
                var shortId = string.IsNullOrWhiteSpace(result.SessionId) ? "-" : result.SessionId[..8];
                var toolLabel = GetToolDisplayName(toolId);
                await _feishuChannel.SendMessageAsync(
                    actualChatKey,
                    $"✅ 已完成：已导入并切换到会话 {shortId}...\n🛠️ CLI 工具: {toolLabel}",
                    username,
                    appId);
            }
            catch (Exception sendEx)
            {
                _logger.LogDebug(sendEx, "[Feishu] 发送导入完成提示失败(可忽略)");
            }

            var response = await HandleOpenSessionManagerAsync(actualChatKey, operatorUserId);
            response.Toast = new CardActionTriggerResponseDto.ToastSuffix
            {
                Content = "✅ 已导入本地会话，并切换为当前会话",
                Type = CardActionTriggerResponseDto.ToastSuffix.ToastType.Success
            };
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入外部 CLI 会话失败");
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 导入失败，请稍后重试。", "error");
        }
    }

    public async Task<ElementsCardV2Dto> BuildProjectManagerCardAsync(string chatId, string? operatorUserId)
    {
        var actualChatKey = NormalizeChatKey(chatId);
        using var projectScope = CreateProjectScopeContext(actualChatKey, operatorUserId)
            ?? throw new InvalidOperationException("请先绑定 Web 用户，再管理项目");
        var projects = await projectScope.ProjectService.GetProjectsAsync();
        return BuildProjectManagerCard(actualChatKey, projects);
    }

    private async Task<CardActionTriggerResponseDto> HandleOpenProjectManagerAsync(string? chatId, string? operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法打开项目管理", "error");
        }

        try
        {
            var card = await BuildProjectManagerCardAsync(chatId, operatorUserId);
            return _cardBuilder.BuildCardActionResponseV2(card, string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse($"❌ {ex.Message}", "error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理打开项目管理失败");
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 项目管理功能暂时不可用，请稍后重试。", "error");
        }
    }

    private Task<CardActionTriggerResponseDto> HandleShowCreateProjectFormAsync(string? chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法打开项目表单", "error"));
        }

        var actualChatKey = NormalizeChatKey(chatId);
        var card = BuildProjectFormCard(actualChatKey, null, null, null, null);
        return Task.FromResult(_cardBuilder.BuildCardActionResponseV2(card, string.Empty));
    }

    private async Task<CardActionTriggerResponseDto> HandleShowEditProjectFormAsync(string? chatId, string? projectId, string? operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(projectId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法编辑项目", "error");
        }

        var actualChatKey = NormalizeChatKey(chatId);
        using var projectScope = CreateProjectScopeContext(actualChatKey, operatorUserId);
        if (projectScope == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再管理项目", "error");
        }

        var project = await projectScope.ProjectService.GetProjectAsync(projectId);
        if (project == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 项目不存在或已被删除", "error");
        }

        var card = BuildProjectFormCard(actualChatKey, project, null, null, "密码或 Token 留空则保持现有值。");
        return _cardBuilder.BuildCardActionResponseV2(card, string.Empty);
    }

    private async Task<CardActionTriggerResponseDto> HandleCreateProjectAsync(string? chatId, JsonElement? formValue, string? operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，创建失败", "error");
        }

        var actualChatKey = NormalizeChatKey(chatId);
        using var projectScope = CreateProjectScopeContext(actualChatKey, operatorUserId);
        if (projectScope == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再管理项目", "error");
        }

        var request = BuildProjectRequestFromForm(formValue);
        if (request.AuthType == "ssh")
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("⚠️ 飞书端当前优先支持 HTTP/HTTPS 项目创建，SSH 项目请在 Web 端维护。", "warning");
        }

        var (project, errorMessage) = await projectScope.ProjectService.CreateProjectAsync(request);
        if (project == null)
        {
            var card = BuildProjectFormCard(actualChatKey, null, request, null, errorMessage);
            return _cardBuilder.BuildCardActionResponseV2(card, errorMessage ?? "创建项目失败", "error");
        }

        var managerCard = await BuildProjectManagerCardAsync(actualChatKey, operatorUserId);
        return _cardBuilder.BuildCardActionResponseV2(managerCard, "✅ 项目已保存，可继续克隆或创建会话", "success");
    }

    private async Task<CardActionTriggerResponseDto> HandleUpdateProjectAsync(string? chatId, string? projectId, JsonElement? formValue, string? operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(projectId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，更新失败", "error");
        }

        var actualChatKey = NormalizeChatKey(chatId);
        using var projectScope = CreateProjectScopeContext(actualChatKey, operatorUserId);
        if (projectScope == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再管理项目", "error");
        }

        var existingProject = await projectScope.ProjectService.GetProjectAsync(projectId);
        if (existingProject == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 项目不存在或已被删除", "error");
        }

        var request = BuildUpdateProjectRequestFromForm(formValue);
        var (success, errorMessage) = await projectScope.ProjectService.UpdateProjectAsync(projectId, request);
        if (!success)
        {
            var requestState = BuildProjectRequestFromForm(formValue);
            var card = BuildProjectFormCard(actualChatKey, existingProject, requestState, null, errorMessage ?? "更新项目失败");
            return _cardBuilder.BuildCardActionResponseV2(card, errorMessage ?? "更新项目失败", "error");
        }

        var managerCard = await BuildProjectManagerCardAsync(actualChatKey, operatorUserId);
        return _cardBuilder.BuildCardActionResponseV2(managerCard, "✅ 项目配置已更新", "success");
    }

    private Task<CardActionTriggerResponseDto> HandleDeleteProjectAsync(string? chatId, string? projectId, string? operatorUserId, string? appId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(projectId))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，删除失败", "error"));
        }

        var actualChatKey = NormalizeChatKey(chatId);
        _ = Task.Run(() => DeleteProjectInBackgroundAsync(actualChatKey, projectId, operatorUserId, appId));
        return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("🚀 已开始后台删除项目，完成后会发送结果", "info"));
    }

    private async Task<CardActionTriggerResponseDto> HandleFetchProjectBranchesAsync(string? chatId, string? projectId, JsonElement? formValue, string? operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法获取分支", "error");
        }

        var actualChatKey = NormalizeChatKey(chatId);
        using var projectScope = CreateProjectScopeContext(actualChatKey, operatorUserId);
        if (projectScope == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再管理项目", "error");
        }

        var formState = BuildProjectRequestFromForm(formValue);
        if (string.IsNullOrWhiteSpace(formState.GitUrl))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("⚠️ 请先填写 Git 仓库地址", "warning");
        }

        var project = string.IsNullOrWhiteSpace(projectId)
            ? null
            : await projectScope.ProjectService.GetProjectAsync(projectId);
        var (branches, errorMessage) = await projectScope.ProjectService.GetBranchesAsync(new GetBranchesRequest
        {
            GitUrl = formState.GitUrl,
            AuthType = formState.AuthType,
            HttpsUsername = formState.HttpsUsername,
            HttpsToken = formState.HttpsToken
        });

        var helperText = errorMessage;
        if (string.IsNullOrWhiteSpace(helperText))
        {
            helperText = branches.Count == 0
                ? "未读取到远程分支，分支留空时将使用远端默认分支。"
                : $"已获取 {branches.Count} 个分支，可从下方列表复制后填写到分支字段。";
        }

        var card = BuildProjectFormCard(actualChatKey, project, formState, branches, helperText);
        var toastType = string.IsNullOrWhiteSpace(errorMessage) ? "info" : "warning";
        var toastMessage = string.IsNullOrWhiteSpace(errorMessage) ? "🔄 已刷新远程分支列表" : $"⚠️ {errorMessage}";
        return _cardBuilder.BuildCardActionResponseV2(card, toastMessage, toastType);
    }

    private Task<CardActionTriggerResponseDto> HandleCloneProjectAsync(string? chatId, string? projectId, string? operatorUserId, string? appId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(projectId))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法克隆项目", "error"));
        }

        var actualChatKey = NormalizeChatKey(chatId);
        _ = Task.Run(async () =>
        {
            string? notificationUsername = null;
            try
            {
                using var projectScope = CreateProjectScopeContext(actualChatKey, operatorUserId);
                if (projectScope == null)
                {
                    await _feishuChannel.SendMessageAsync(actualChatKey, "❌ 请先绑定 Web 用户，再管理项目", appId: appId);
                    return;
                }

                notificationUsername = projectScope.Username;
                var project = await projectScope.ProjectService.GetProjectAsync(projectId);
                var projectName = project?.Name ?? projectId;
                var (success, errorMessage) = await projectScope.ProjectService.CloneProjectAsync(projectId);
                var message = success
                    ? $"✅ 项目 {projectName} 克隆完成"
                    : $"❌ 项目 {projectName} 克隆失败：{errorMessage ?? "未知错误"}";
                await _feishuChannel.SendMessageAsync(actualChatKey, message, notificationUsername, appId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台克隆项目失败: ProjectId={ProjectId}", projectId);
                await _feishuChannel.SendMessageAsync(actualChatKey, $"❌ 项目克隆失败：{ex.Message}", notificationUsername, appId);
            }
        });

        return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("🚀 已开始后台克隆项目，完成后会发送通知", "info"));
    }

    private Task<CardActionTriggerResponseDto> HandlePullProjectAsync(string? chatId, string? projectId, string? operatorUserId, string? appId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(projectId))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法拉取项目", "error"));
        }

        var actualChatKey = NormalizeChatKey(chatId);
        _ = Task.Run(async () =>
        {
            string? notificationUsername = null;
            try
            {
                using var projectScope = CreateProjectScopeContext(actualChatKey, operatorUserId);
                if (projectScope == null)
                {
                    await _feishuChannel.SendMessageAsync(actualChatKey, "❌ 请先绑定 Web 用户，再管理项目", appId: appId);
                    return;
                }

                notificationUsername = projectScope.Username;
                var project = await projectScope.ProjectService.GetProjectAsync(projectId);
                var projectName = project?.Name ?? projectId;
                var (success, errorMessage) = await projectScope.ProjectService.PullProjectAsync(projectId);
                var message = success
                    ? $"✅ 项目 {projectName} 已拉取最新代码"
                    : $"❌ 项目 {projectName} 拉取失败：{errorMessage ?? "未知错误"}";
                await _feishuChannel.SendMessageAsync(actualChatKey, message, notificationUsername, appId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台拉取项目失败: ProjectId={ProjectId}", projectId);
                await _feishuChannel.SendMessageAsync(actualChatKey, $"❌ 项目拉取失败：{ex.Message}", notificationUsername, appId);
            }
        });

        return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("🚀 已开始后台拉取项目，完成后会发送通知", "info"));
    }

    private Task<CardActionTriggerResponseDto> HandleShowProjectBranchSwitcherAsync(
        string? chatId,
        string? projectId,
        int? page,
        string? operatorUserId,
        string? appId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(projectId))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法选择项目分支", "error"));
        }

        var actualChatKey = NormalizeChatKey(chatId);
        _ = Task.Run(() => SendProjectBranchSwitcherCardAsync(actualChatKey, projectId, page, operatorUserId, appId));
        return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("🚀 已开始后台加载分支列表，完成后会发送卡片", "info"));
    }

    private Task<CardActionTriggerResponseDto> HandleSwitchProjectBranchAsync(
        string? chatId,
        string? projectId,
        string? branch,
        int? page,
        string? operatorUserId,
        string? appId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(branch))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法切换项目分支", "error"));
        }

        var actualChatKey = NormalizeChatKey(chatId);
        _ = Task.Run(() => SwitchProjectBranchInBackgroundAsync(actualChatKey, projectId, branch, page, operatorUserId, appId));
        return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse($"🚀 已开始后台切换到分支 {branch}", "info"));
    }

    private async Task SendProjectBranchSwitcherCardAsync(string chatKey, string projectId, int? page, string? operatorUserId, string? appId)
    {
        string? notificationUsername = null;
        try
        {
            using var projectScope = CreateProjectScopeContext(chatKey, operatorUserId);
            if (projectScope == null)
            {
                await _feishuChannel.SendMessageAsync(chatKey, "❌ 请先绑定 Web 用户，再管理项目", appId: appId);
                return;
            }

            notificationUsername = projectScope.Username;
            var project = await projectScope.ProjectService.GetProjectAsync(projectId);
            if (project == null)
            {
                await _feishuChannel.SendMessageAsync(chatKey, "❌ 项目不存在或已被删除", notificationUsername, appId);
                return;
            }

            var (branches, errorMessage) = await projectScope.ProjectService.GetProjectBranchesAsync(projectId);
            var card = BuildProjectBranchSwitcherCard(chatKey, project, branches, errorMessage, page ?? 0);
            await SendElementsCardToChatAsync(chatKey, card, "❌ 分支列表加载完成，但发送卡片失败", notificationUsername, appId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "后台加载项目分支列表失败: ChatKey={ChatKey}, ProjectId={ProjectId}", chatKey, projectId);
            await _feishuChannel.SendMessageAsync(chatKey, $"❌ 加载项目分支列表失败：{ex.Message}", notificationUsername, appId);
        }
    }

    private async Task SwitchProjectBranchInBackgroundAsync(
        string chatKey,
        string projectId,
        string branch,
        int? page,
        string? operatorUserId,
        string? appId)
    {
        string? notificationUsername = null;
        try
        {
            using var projectScope = CreateProjectScopeContext(chatKey, operatorUserId);
            if (projectScope == null)
            {
                await _feishuChannel.SendMessageAsync(chatKey, "❌ 请先绑定 Web 用户，再管理项目", appId: appId);
                return;
            }

            notificationUsername = projectScope.Username;
            var project = await projectScope.ProjectService.GetProjectAsync(projectId);
            if (project == null)
            {
                await _feishuChannel.SendMessageAsync(chatKey, "❌ 项目不存在或已被删除", notificationUsername, appId);
                return;
            }

            var (success, errorMessage) = await projectScope.ProjectService.SwitchProjectBranchAsync(projectId, branch);
            if (!success)
            {
                var latestProject = await projectScope.ProjectService.GetProjectAsync(projectId) ?? project;
                var (branches, branchErrorMessage) = await projectScope.ProjectService.GetProjectBranchesAsync(projectId);
                var helperText = errorMessage;
                if (!string.IsNullOrWhiteSpace(branchErrorMessage) && !string.Equals(branchErrorMessage, errorMessage, StringComparison.Ordinal))
                {
                    helperText = string.IsNullOrWhiteSpace(helperText)
                        ? branchErrorMessage
                        : $"{helperText}；{branchErrorMessage}";
                }

                var retryCard = BuildProjectBranchSwitcherCard(chatKey, latestProject, branches, helperText, page ?? 0);
                await SendElementsCardToChatAsync(chatKey, retryCard, $"❌ 切换分支失败：{errorMessage ?? "未知错误"}", notificationUsername, appId);
                return;
            }

            var managerCard = await BuildProjectManagerCardAsync(chatKey, operatorUserId);
            await SendElementsCardToChatAsync(chatKey, managerCard, $"✅ 已切换到分支 {branch}", notificationUsername, appId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "后台切换项目分支失败: ChatKey={ChatKey}, ProjectId={ProjectId}, Branch={Branch}", chatKey, projectId, branch);
            await _feishuChannel.SendMessageAsync(chatKey, $"❌ 切换项目分支失败：{ex.Message}", notificationUsername, appId);
        }
    }

    private async Task DeleteProjectInBackgroundAsync(string chatKey, string projectId, string? operatorUserId, string? appId)
    {
        string? notificationUsername = null;
        try
        {
            using var projectScope = CreateProjectScopeContext(chatKey, operatorUserId);
            if (projectScope == null)
            {
                await _feishuChannel.SendMessageAsync(chatKey, "❌ 请先绑定 Web 用户，再管理项目", appId: appId);
                return;
            }

            notificationUsername = projectScope.Username;
            var project = await projectScope.ProjectService.GetProjectAsync(projectId);
            var projectName = project?.Name ?? projectId;
            var (success, errorMessage) = await projectScope.ProjectService.DeleteProjectAsync(projectId);
            if (!success)
            {
                await _feishuChannel.SendMessageAsync(chatKey, $"❌ 项目 {projectName} 删除失败：{errorMessage ?? "未知错误"}", notificationUsername, appId);
                return;
            }

            var managerCard = await BuildProjectManagerCardAsync(chatKey, operatorUserId);
            await SendElementsCardToChatAsync(chatKey, managerCard, $"🗑️ 项目 {projectName} 已删除", notificationUsername, appId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "后台删除项目失败: ChatKey={ChatKey}, ProjectId={ProjectId}", chatKey, projectId);
            await _feishuChannel.SendMessageAsync(chatKey, $"❌ 项目删除失败：{ex.Message}", notificationUsername, appId);
        }
    }

    private async Task<CardActionTriggerResponseDto> HandleCreateSessionFromProjectAsync(string? chatId, string? projectId, string? operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(projectId))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法基于项目创建会话", "error");
        }

        var actualChatKey = NormalizeChatKey(chatId);
        using var projectScope = CreateProjectScopeContext(actualChatKey, operatorUserId);
        if (projectScope == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再管理项目", "error");
        }

        var project = await projectScope.ProjectService.GetProjectAsync(projectId);
        if (project == null)
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("❌ 项目不存在或已被删除", "error");
        }

        if (!string.Equals(project.Status, "ready", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(project.LocalPath) || !Directory.Exists(project.LocalPath))
        {
            return _cardBuilder.BuildCardActionToastOnlyResponse("⚠️ 项目尚未就绪，请先完成克隆", "warning");
        }

        var toolId = ResolveToolIdForChat(actualChatKey, projectScope.Username);
        var newSessionId = _feishuChannel.CreateNewSession(new FeishuIncomingMessage
        {
            ChatId = actualChatKey,
            SenderName = projectScope.Username
        }, project.LocalPath, toolId);

        _feishuChannel.SwitchCurrentSession(actualChatKey, newSessionId, projectScope.Username);
        var managerCard = await BuildProjectManagerCardAsync(actualChatKey, operatorUserId);
        return _cardBuilder.BuildCardActionResponseV2(
            managerCard,
            $"✅ 已基于项目 {project.Name} 创建并切换到新会话 {newSessionId[..8]}...",
            "success");
    }

    private ElementsCardV2Dto BuildProjectManagerCard(string chatKey, List<ProjectInfo> projects)
    {
        var elements = new List<object>
        {
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"## 📁 项目管理\n当前共有 **{projects.Count}** 个项目。\n支持 Git/TFS 项目；`https` 认证表示 **HTTP/HTTPS 用户名 + 密码或 Token**。\n分支留空时将使用远端默认分支。"
                }
            },
            new { tag = "hr" },
            BuildActionButton(
                "➕ 新建项目",
                "primary",
                new
                {
                    action = "show_create_project_form",
                    chat_key = chatKey
                })
        };

        if (projects.Count == 0)
        {
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "plain_text", content = "暂无项目，可先创建一个 Git/TFS 项目。" }
            });
        }
        else
        {
            foreach (var project in projects.Take(12))
            {
                var branchDisplay = string.IsNullOrWhiteSpace(project.Branch) ? "远端默认分支" : project.Branch;
                var lastSyncText = project.LastSyncAt.HasValue
                    ? project.LastSyncAt.Value.ToString("yyyy-MM-dd HH:mm")
                    : "未同步";
                var gitUrlDisplay = string.IsNullOrWhiteSpace(project.GitUrl) ? "(ZIP 项目)" : project.GitUrl;

                elements.Add(new { tag = "hr" });
                elements.Add(new
                {
                    tag = "div",
                    text = new
                    {
                        tag = "lark_md",
                        content = $"### {GetProjectStatusLabel(project)} {project.Name}\n仓库: `{gitUrlDisplay}`\n分支: `{branchDisplay}`\n状态: `{project.Status}` · 最后同步: `{lastSyncText}`"
                    }
                });

                if (!string.IsNullOrWhiteSpace(project.ErrorMessage))
                {
                    elements.Add(new
                    {
                        tag = "div",
                        text = new
                        {
                            tag = "lark_md",
                            content = $"**错误信息**\n{project.ErrorMessage}"
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(project.GitUrl))
                {
                    elements.Add(BuildActionButton(
                        string.Equals(project.Status, "ready", StringComparison.OrdinalIgnoreCase) ? "🔄 拉取最新代码" : "📥 克隆项目",
                        "default",
                        new
                        {
                            action = string.Equals(project.Status, "ready", StringComparison.OrdinalIgnoreCase) ? "pull_project" : "clone_project",
                            chat_key = chatKey,
                            project_id = project.ProjectId
                        }));

                    if (string.Equals(project.Status, "ready", StringComparison.OrdinalIgnoreCase))
                    {
                        elements.Add(BuildActionButton(
                            "🌿 切换分支",
                            "default",
                            new
                            {
                                action = "show_project_branch_switcher",
                                chat_key = chatKey,
                                project_id = project.ProjectId
                            }));
                    }

                    elements.Add(BuildActionButton(
                        "✏️ 编辑项目",
                        "default",
                        new
                        {
                            action = "show_edit_project_form",
                            chat_key = chatKey,
                            project_id = project.ProjectId
                        }));
                }

                if (string.Equals(project.Status, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    elements.Add(BuildActionButton(
                        "🆕 基于项目创建会话",
                        "primary",
                        new
                        {
                            action = "create_session_from_project",
                            chat_key = chatKey,
                            project_id = project.ProjectId
                        }));
                }

                elements.Add(BuildActionButton(
                    "🗑️ 删除项目",
                    "danger",
                    new
                    {
                        action = "delete_project",
                        chat_key = chatKey,
                        project_id = project.ProjectId
                    }));
            }
        }

        elements.Add(new { tag = "hr" });
        elements.Add(BuildActionButton(
            "📋 返回会话管理",
            "default",
            new
            {
                action = "open_session_manager"
            }));

        return new ElementsCardV2Dto
        {
            Header = new ElementsCardV2Dto.HeaderSuffix
            {
                Template = "green",
                Title = new HeaderTitleElement { Content = "📁 项目管理" }
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
    }

    private ElementsCardV2Dto BuildProjectBranchSwitcherCard(
        string chatKey,
        ProjectInfo project,
        List<string> branches,
        string? helperText,
        int pageIndex)
    {
        var normalizedBranches = branches
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalPages = Math.Max(1, (int)Math.Ceiling(normalizedBranches.Count / (double)ProjectBranchPageSize));
        var safePageIndex = Math.Clamp(pageIndex, 0, totalPages - 1);
        var pagedBranches = normalizedBranches
            .Skip(safePageIndex * ProjectBranchPageSize)
            .Take(ProjectBranchPageSize)
            .ToList();
        var branchDisplay = string.IsNullOrWhiteSpace(project.Branch) ? "远端默认分支" : project.Branch;
        var gitUrlDisplay = string.IsNullOrWhiteSpace(project.GitUrl) ? "(ZIP 项目)" : project.GitUrl;
        var elements = new List<object>
        {
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"## 🌿 切换项目分支\n项目: **{project.Name}**\n仓库: `{gitUrlDisplay}`\n当前分支: `{branchDisplay}`\n远程分支: **{normalizedBranches.Count}** 个，第 **{safePageIndex + 1}/{totalPages}** 页\n点击目标分支后会直接执行切换。"
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(helperText))
        {
            elements.Add(new { tag = "hr" });
            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"**提示**\n{helperText}"
                }
            });
        }

        elements.Add(new { tag = "hr" });

        if (pagedBranches.Count == 0)
        {
            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = "当前没有可展示的远程分支。"
                }
            });
        }
        else
        {
            foreach (var branch in pagedBranches)
            {
                var isCurrentBranch = string.Equals(project.Branch, branch, StringComparison.OrdinalIgnoreCase);
                elements.Add(new
                {
                    tag = "column_set",
                    flex_mode = "none",
                    background_style = "default",
                    columns = new object[]
                    {
                        new
                        {
                            tag = "column",
                            width = "weighted",
                            weight = 5,
                            vertical_align = "top",
                            elements = new object[]
                            {
                                new
                                {
                                    tag = "div",
                                    text = new
                                    {
                                        tag = "lark_md",
                                        content = isCurrentBranch
                                            ? $"**`{branch}`**\n当前所在分支"
                                            : $"**`{branch}`**"
                                    }
                                }
                            }
                        },
                        new
                        {
                            tag = "column",
                            width = "auto",
                            vertical_align = "top",
                            elements = isCurrentBranch
                                ? new object[]
                                {
                                    new
                                    {
                                        tag = "div",
                                        text = new { tag = "plain_text", content = "当前分支" }
                                    }
                                }
                                : new object[]
                                {
                                    BuildActionButton(
                                        "切换到此分支",
                                        "primary",
                                        new
                                        {
                                            action = "switch_project_branch",
                                            chat_key = chatKey,
                                            project_id = project.ProjectId,
                                            branch,
                                            page = safePageIndex
                                        })
                                }
                        }
                    }
                });
            }
        }

        if (totalPages > 1)
        {
            elements.Add(new { tag = "hr" });
            elements.Add(new
            {
                tag = "column_set",
                flex_mode = "none",
                background_style = "default",
                columns = new object[]
                {
                    new
                    {
                        tag = "column",
                        width = "weighted",
                        weight = 2,
                        vertical_align = "top",
                        elements = safePageIndex > 0
                            ? new object[]
                            {
                                BuildActionButton(
                                    "⬅️ 上一页",
                                    "default",
                                    new
                                    {
                                        action = "show_project_branch_switcher",
                                        chat_key = chatKey,
                                        project_id = project.ProjectId,
                                        page = safePageIndex - 1
                                    })
                            }
                            : new object[]
                            {
                                new
                                {
                                    tag = "div",
                                    text = new { tag = "plain_text", content = string.Empty }
                                }
                            }
                    },
                    new
                    {
                        tag = "column",
                        width = "weighted",
                        weight = 3,
                        vertical_align = "top",
                        elements = new object[]
                        {
                            new
                            {
                                tag = "div",
                                text = new
                                {
                                    tag = "lark_md",
                                    content = $"当前第 **{safePageIndex + 1}/{totalPages}** 页"
                                }
                            }
                        }
                    },
                    new
                    {
                        tag = "column",
                        width = "weighted",
                        weight = 2,
                        vertical_align = "top",
                        elements = safePageIndex < totalPages - 1
                            ? new object[]
                            {
                                BuildActionButton(
                                    "下一页 ➡️",
                                    "default",
                                    new
                                    {
                                        action = "show_project_branch_switcher",
                                        chat_key = chatKey,
                                        project_id = project.ProjectId,
                                        page = safePageIndex + 1
                                    })
                            }
                            : new object[]
                            {
                                new
                                {
                                    tag = "div",
                                    text = new { tag = "plain_text", content = string.Empty }
                                }
                            }
                    }
                }
            });
        }

        elements.Add(new { tag = "hr" });
        elements.Add(BuildActionButton(
            "📁 返回项目列表",
            "default",
            new
            {
                action = "open_project_manager",
                chat_key = chatKey
            }));

        return new ElementsCardV2Dto
        {
            Header = new ElementsCardV2Dto.HeaderSuffix
            {
                Template = "green",
                Title = new HeaderTitleElement { Content = $"🌿 切换分支：{project.Name}" }
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
    }

    private ElementsCardV2Dto BuildProjectFormCard(
        string chatKey,
        ProjectInfo? project,
        CreateProjectRequest? formState,
        List<string>? branchSuggestions,
        string? helperText)
    {
        var effectiveState = formState ?? new CreateProjectRequest
        {
            Name = project?.Name ?? string.Empty,
            GitUrl = project?.GitUrl ?? string.Empty,
            AuthType = project?.AuthType ?? "https",
            Branch = project?.Branch ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(effectiveState.AuthType))
        {
            effectiveState.AuthType = "https";
        }

        var actionName = project == null ? "create_project" : "update_project";
        var submitButtonName = project == null
            ? "create_project_submit"
            : $"update_project_submit__{project.ProjectId}";
        var fetchBranchesButtonName = project == null
            ? "fetch_project_branches_submit"
            : $"fetch_project_branches_submit__{project.ProjectId}";
        var title = project == null ? "📁 新建项目" : $"✏️ 编辑项目：{project.Name}";
        var tips = new List<string>
        {
            "支持 `none` / `https` 两种常用模式；TFS 请填写 `https`。",
            "`https` 表示 HTTP/HTTPS 用户名 + 密码或 Token。",
            "分支留空时将使用远端默认分支。",
            "如果你在此卡片里刷新了分支列表，提交前需要重新输入密码或 Token。"
        };

        if (!string.IsNullOrWhiteSpace(helperText))
        {
            tips.Add(helperText);
        }

        var elements = new List<object>
        {
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"## {title}\n{string.Join("\n", tips.Select(tip => $"- {tip}"))}"
                }
            },
            new { tag = "hr" },
            new
            {
                tag = "form",
                name = project == null ? "create_project_form" : "update_project_form",
                elements = new object[]
                {
                    new
                    {
                        tag = "input",
                        input_type = "text",
                        name = "project_name",
                        label = new { tag = "plain_text", content = "项目名称" },
                        placeholder = new { tag = "plain_text", content = "例如：WmsServerV4" },
                        default_value = effectiveState.Name
                    },
                    new
                    {
                        tag = "input",
                        input_type = "text",
                        name = "project_git_url",
                        label = new { tag = "plain_text", content = "Git 仓库地址" },
                        placeholder = new { tag = "plain_text", content = "http://sql-for-tfs2017:8080/tfs/..." },
                        default_value = effectiveState.GitUrl
                    },
                    new
                    {
                        tag = "input",
                        input_type = "text",
                        name = "project_auth_type",
                        label = new { tag = "plain_text", content = "认证方式" },
                        placeholder = new { tag = "plain_text", content = "none 或 https" },
                        default_value = effectiveState.AuthType
                    },
                    new
                    {
                        tag = "input",
                        input_type = "text",
                        name = "project_https_username",
                        label = new { tag = "plain_text", content = "HTTP/HTTPS 用户名" },
                        placeholder = new { tag = "plain_text", content = "TFS 账号；Token 场景可留空" },
                        default_value = effectiveState.HttpsUsername ?? string.Empty
                    },
                    new
                    {
                        tag = "input",
                        input_type = "password",
                        name = "project_https_token",
                        label = new { tag = "plain_text", content = "密码或 Token" },
                        placeholder = new { tag = "plain_text", content = "请输入密码或 Token" }
                    },
                    new
                    {
                        tag = "input",
                        input_type = "text",
                        name = "project_branch",
                        label = new { tag = "plain_text", content = "分支（可留空）" },
                        placeholder = new { tag = "plain_text", content = "留空使用远端默认分支" },
                        default_value = effectiveState.Branch ?? string.Empty
                    },
                    new
                    {
                        tag = "column_set",
                        flex_mode = "none",
                        background_style = "default",
                        columns = new object[]
                        {
                            new
                            {
                                tag = "column",
                                width = "auto",
                                vertical_align = "top",
                                elements = new object[]
                                {
                                    new
                                    {
                                        tag = "button",
                                        text = new { tag = "plain_text", content = "刷新远程分支" },
                                        type = "default",
                                        action_type = "form_submit",
                                        name = fetchBranchesButtonName,
                                        value = new
                                        {
                                            action = "fetch_project_branches",
                                            chat_key = chatKey,
                                            project_id = project?.ProjectId
                                        }
                                    }
                                }
                            },
                            new
                            {
                                tag = "column",
                                width = "auto",
                                vertical_align = "top",
                                elements = new object[]
                                {
                                    new
                                    {
                                        tag = "button",
                                        text = new { tag = "plain_text", content = project == null ? "保存项目" : "保存修改" },
                                        type = "primary",
                                        action_type = "form_submit",
                                        name = submitButtonName,
                                        value = new
                                        {
                                            action = actionName,
                                            chat_key = chatKey,
                                            project_id = project?.ProjectId
                                        }
                                    }
                                }
                            },
                            new
                            {
                                tag = "column",
                                width = "auto",
                                vertical_align = "top",
                                elements = new object[]
                                {
                                    new
                                    {
                                        tag = "button",
                                        text = new { tag = "plain_text", content = "返回项目列表" },
                                        type = "default",
                                        behaviors = new[]
                                        {
                                            new
                                            {
                                                type = "callback",
                                                value = new
                                                {
                                                    action = "open_project_manager",
                                                    chat_key = chatKey
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        if (branchSuggestions is { Count: > 0 })
        {
            elements.Add(new { tag = "hr" });
            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"**远程分支**\n{string.Join("\n", branchSuggestions.Select(branch => $"- `{branch}`"))}"
                }
            });
        }

        return new ElementsCardV2Dto
        {
            Header = new ElementsCardV2Dto.HeaderSuffix
            {
                Template = "orange",
                Title = new HeaderTitleElement { Content = title }
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
    }

    private Task<CardActionTriggerResponseDto> HandleBrowseCurrentSessionDirectoryAsync(string? chatKey, string? chatId, string? operatorUserId)
    {
        var actualChatKey = !string.IsNullOrWhiteSpace(chatKey)
            ? NormalizeChatKey(chatKey)
            : (!string.IsNullOrWhiteSpace(chatId) ? NormalizeChatKey(chatId) : string.Empty);

        if (string.IsNullOrWhiteSpace(actualChatKey))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，无法浏览当前会话目录", "error"));
        }

        var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再浏览当前会话目录", "error"));
        }

        var currentSessionId = _feishuChannel.GetCurrentSession(actualChatKey, username);
        if (string.IsNullOrWhiteSpace(currentSessionId))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("⚠️ 当前没有活跃会话，请先切换或创建会话", "warning"));
        }

        return HandleBrowseSessionDirectoryAsync(currentSessionId, actualChatKey, null, 0, operatorUserId);
    }

    private Task<CardActionTriggerResponseDto> HandleBrowseSessionDirectoryAsync(string? sessionId, string? chatKey, string? directoryPath, int? page, string? operatorUserId)
    {
        if (!TryResolveActiveSessionContext(sessionId, chatKey, operatorUserId, out var actualChatKey, out var activeSessionId, out var errorResponse))
        {
            return Task.FromResult(errorResponse!);
        }

        try
        {
            var workspacePath = _cliExecutor.GetSessionWorkspacePath(activeSessionId);
            if (!Directory.Exists(workspacePath))
            {
                return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 当前会话工作目录不存在或已失效", "error"));
            }

            var normalizedDirectoryPath = NormalizeWorkspaceRelativePath(directoryPath);
            var fullDirectoryPath = ResolveWorkspaceFullPath(workspacePath, normalizedDirectoryPath);
            if (!Directory.Exists(fullDirectoryPath))
            {
                return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 目录不存在或已被删除", "error"));
            }

            var entries = GetDirectoryEntries(workspacePath, fullDirectoryPath);
            var totalPages = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)SessionDirectoryPageSize));
            var pageIndex = Math.Clamp(page ?? 0, 0, totalPages - 1);
            var pagedEntries = entries
                .Skip(pageIndex * SessionDirectoryPageSize)
                .Take(SessionDirectoryPageSize)
                .ToList();

            var card = BuildSessionDirectoryCard(
                actualChatKey,
                activeSessionId,
                workspacePath,
                normalizedDirectoryPath,
                pageIndex,
                totalPages,
                entries.Count,
                pagedEntries);

            return Task.FromResult(_cardBuilder.BuildCardActionResponseV2(card, string.Empty));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "[Feishu] 浏览会话目录被拒绝: SessionId={SessionId}, DirectoryPath={DirectoryPath}", activeSessionId, directoryPath);
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 无法访问该目录", "error"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Feishu] 浏览会话目录失败: SessionId={SessionId}, DirectoryPath={DirectoryPath}", activeSessionId, directoryPath);
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 目录浏览失败，请稍后重试", "error"));
        }
    }

    private Task<CardActionTriggerResponseDto> HandlePreviewSessionFileAsync(string? sessionId, string? chatKey, string? filePath, string? directoryPath, int? page, string? operatorUserId)
    {
        if (!TryResolveActiveSessionContext(sessionId, chatKey, operatorUserId, out var actualChatKey, out var activeSessionId, out var errorResponse))
        {
            return Task.FromResult(errorResponse!);
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 文件路径不能为空", "error"));
        }

        try
        {
            var workspacePath = _cliExecutor.GetSessionWorkspacePath(activeSessionId);
            if (!Directory.Exists(workspacePath))
            {
                return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 当前会话工作目录不存在或已失效", "error"));
            }

            var normalizedFilePath = NormalizeWorkspaceRelativePath(filePath);
            var fullFilePath = ResolveWorkspaceFullPath(workspacePath, normalizedFilePath);
            if (!File.Exists(fullFilePath))
            {
                return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 文件不存在或已被删除", "error"));
            }

            var fileBytes = _cliExecutor.GetWorkspaceFile(activeSessionId, normalizedFilePath);
            if (fileBytes == null)
            {
                return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 文件读取失败，请稍后重试", "error"));
            }

            var card = BuildSessionFilePreviewCard(
                actualChatKey,
                activeSessionId,
                workspacePath,
                normalizedFilePath,
                NormalizeWorkspaceRelativePath(directoryPath),
                Math.Max(page ?? 0, 0),
                fileBytes);

            return Task.FromResult(_cardBuilder.BuildCardActionResponseV2(card, string.Empty));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "[Feishu] 预览会话文件被拒绝: SessionId={SessionId}, FilePath={FilePath}", activeSessionId, filePath);
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 无法访问该文件", "error"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Feishu] 预览会话文件失败: SessionId={SessionId}, FilePath={FilePath}", activeSessionId, filePath);
            return Task.FromResult(_cardBuilder.BuildCardActionToastOnlyResponse("❌ 文件预览失败，请稍后重试", "error"));
        }
    }

    private bool TryResolveActiveSessionContext(
        string? sessionId,
        string? chatKey,
        string? operatorUserId,
        out string actualChatKey,
        out string activeSessionId,
        out CardActionTriggerResponseDto? errorResponse)
    {
        actualChatKey = string.Empty;
        activeSessionId = string.Empty;
        errorResponse = null;

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(chatKey))
        {
            errorResponse = _cardBuilder.BuildCardActionToastOnlyResponse("❌ 参数错误，请重新打开会话管理后再试", "error");
            return false;
        }

        actualChatKey = NormalizeChatKey(chatKey);
        var username = ResolveFeishuUsername(actualChatKey, operatorUserId);
        if (string.IsNullOrWhiteSpace(username))
        {
            errorResponse = _cardBuilder.BuildCardActionToastOnlyResponse("❌ 请先绑定 Web 用户，再浏览会话目录", "error");
            return false;
        }

        var currentSessionId = _feishuChannel.GetCurrentSession(actualChatKey, username);
        if (string.IsNullOrWhiteSpace(currentSessionId))
        {
            errorResponse = _cardBuilder.BuildCardActionToastOnlyResponse("⚠️ 当前没有活跃会话，请先切换或创建会话", "warning");
            return false;
        }

        if (!string.Equals(currentSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
        {
            errorResponse = _cardBuilder.BuildCardActionToastOnlyResponse("⚠️ 当前会话已变化，请重新打开目录浏览", "warning");
            return false;
        }

        activeSessionId = currentSessionId;
        return true;
    }

    private ElementsCardV2Dto BuildAllowedDirectoryCard(
        string chatKey,
        string toolId,
        AllowedDirectoryBrowseResult browseResult,
        int pageIndex)
    {
        var elements = new List<object>
        {
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = "## 📁 浏览白名单目录\n优先展示当前用户配置的白名单目录；未单独配置时回退到 `Workspace:AllowedRoots` 全局目录。"
                }
            },
            new { tag = "hr" }
        };

        if (!browseResult.HasConfiguredRoots)
        {
            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = "当前用户还没有可浏览的白名单目录，且系统也没有可回退的全局目录。请先在用户管理中配置白名单目录，或在服务端设置 `Workspace:AllowedRoots`。"
                }
            });
        }
        else if (string.IsNullOrWhiteSpace(browseResult.CurrentPath))
        {
            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"可浏览根目录数量：`{browseResult.Roots.Count}`"
                }
            });

            if (browseResult.Roots.Count == 0)
            {
                elements.Add(new
                {
                    tag = "div",
                    text = new { tag = "plain_text", content = "白名单目录已配置，但当前没有可浏览目录。" }
                });
            }
            else
            {
                foreach (var root in browseResult.Roots)
                {
                    elements.Add(BuildAllowedDirectoryRootRow(root, chatKey, toolId));
                }
            }
        }
        else
        {
            var totalPages = Math.Max(1, (int)Math.Ceiling(browseResult.Entries.Count / (double)SessionDirectoryPageSize));
            var clampedPageIndex = Math.Clamp(pageIndex, 0, totalPages - 1);
            var pagedEntries = browseResult.Entries
                .Skip(clampedPageIndex * SessionDirectoryPageSize)
                .Take(SessionDirectoryPageSize)
                .ToList();

            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"当前目录: `{browseResult.CurrentPath}`\n根目录: `{browseResult.RootPath}`\n共 `{browseResult.Entries.Count}` 项，第 `{clampedPageIndex + 1}/{totalPages}` 页"
                }
            });

            elements.Add(BuildActionButton(
                "✅ 使用当前目录创建会话",
                "primary",
                new
                {
                    action = "create_session",
                    create_mode = "existing",
                    workspace_path = browseResult.CurrentPath,
                    chat_key = chatKey,
                    tool_id = toolId
                }));

            elements.Add(BuildActionButton(
                "📋 发送当前目录路径",
                "default",
                new
                {
                    action = "copy_path_to_chat",
                    chat_key = chatKey,
                    copy_path = browseResult.CurrentPath
                }));

            if (pagedEntries.Count == 0)
            {
                elements.Add(new
                {
                    tag = "div",
                    text = new { tag = "plain_text", content = "当前目录为空" }
                });
            }
            else
            {
                foreach (var entry in pagedEntries)
                {
                    elements.Add(BuildAllowedDirectoryEntryRow(entry, chatKey, toolId));
                }
            }

            if (!string.IsNullOrWhiteSpace(browseResult.ParentPath))
            {
                elements.Add(BuildActionButton(
                    "⬆️ 返回上一级",
                    "default",
                    new
                    {
                        action = "browse_allowed_directory",
                        chat_key = chatKey,
                        workspace_path = browseResult.ParentPath,
                        tool_id = toolId
                    }));
            }
            else
            {
                elements.Add(BuildActionButton(
                    "🏠 返回白名单根目录",
                    "default",
                    new
                    {
                        action = "browse_allowed_directory",
                        chat_key = chatKey,
                        tool_id = toolId
                    }));
            }

            if (clampedPageIndex > 0)
            {
                elements.Add(BuildActionButton(
                    "⬅️ 上一页",
                    "default",
                    new
                    {
                        action = "browse_allowed_directory",
                        chat_key = chatKey,
                        workspace_path = browseResult.CurrentPath,
                        page = clampedPageIndex - 1,
                        tool_id = toolId
                    }));
            }

            if (clampedPageIndex + 1 < totalPages)
            {
                elements.Add(BuildActionButton(
                    "➡️ 下一页",
                    "default",
                    new
                    {
                        action = "browse_allowed_directory",
                        chat_key = chatKey,
                        workspace_path = browseResult.CurrentPath,
                        page = clampedPageIndex + 1,
                        tool_id = toolId
                    }));
            }
        }

        elements.Add(new { tag = "hr" });
        elements.Add(BuildActionButton(
            "⬅️ 返回新建会话",
            "default",
            new
            {
                action = "show_create_session_form",
                chat_key = chatKey,
                tool_id = toolId
            }));

        return new ElementsCardV2Dto
        {
            Header = new ElementsCardV2Dto.HeaderSuffix
            {
                Template = "blue",
                Title = new HeaderTitleElement { Content = "📁 浏览白名单目录" }
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
    }

    private ElementsCardV2Dto BuildSessionDirectoryCard(
        string chatKey,
        string sessionId,
        string workspacePath,
        string directoryPath,
        int pageIndex,
        int totalPages,
        int totalEntries,
        List<SessionDirectoryEntry> entries)
    {
        var displayPath = GetDirectoryDisplayPath(directoryPath);
        var currentDirectoryFullPath = BuildWorkspaceEntryFullPath(workspacePath, directoryPath);
        var elements = new List<object>
        {
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"## 📂 当前会话目录\n会话: `{GetShortSessionLabel(sessionId)}`\n工作区: `{workspacePath}`\n当前位置: `{displayPath}`\n共 `{totalEntries}` 项，第 `{pageIndex + 1}/{totalPages}` 页"
                }
            },
            BuildActionButton(
                "📋 发送当前目录路径",
                "default",
                new
                {
                    action = "copy_path_to_chat",
                    chat_key = chatKey,
                    copy_path = currentDirectoryFullPath
                }),
            new { tag = "hr" }
        };

        if (entries.Count == 0)
        {
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "plain_text", content = "当前目录为空" }
            });
        }
        else
        {
            foreach (var entry in entries)
            {
                elements.Add(BuildSessionDirectoryEntryRow(entry, chatKey, sessionId, directoryPath, pageIndex));
            }
        }

        var parentDirectoryPath = GetParentDirectoryPath(directoryPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            elements.Add(BuildActionButton(
                "⬆️ 返回上一级",
                "default",
                new
                {
                    action = "browse_session_directory",
                    chat_key = chatKey,
                    session_id = sessionId,
                    directory_path = parentDirectoryPath,
                    page = 0
                }));
        }

        if (pageIndex > 0)
        {
            elements.Add(BuildActionButton(
                "⬅️ 上一页",
                "default",
                new
                {
                    action = "browse_session_directory",
                    chat_key = chatKey,
                    session_id = sessionId,
                    directory_path = directoryPath,
                    page = pageIndex - 1
                }));
        }

        if (pageIndex + 1 < totalPages)
        {
            elements.Add(BuildActionButton(
                "➡️ 下一页",
                "default",
                new
                {
                    action = "browse_session_directory",
                    chat_key = chatKey,
                    session_id = sessionId,
                    directory_path = directoryPath,
                    page = pageIndex + 1
                }));
        }

        elements.Add(BuildActionButton(
            "📋 返回会话管理",
            "primary",
            new
            {
                action = "open_session_manager"
            }));

        return new ElementsCardV2Dto
        {
            Header = new ElementsCardV2Dto.HeaderSuffix
            {
                Template = "blue",
                Title = new HeaderTitleElement { Content = "📂 当前会话目录" }
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
    }

    private ElementsCardV2Dto BuildSessionFilePreviewCard(
        string chatKey,
        string sessionId,
        string workspacePath,
        string filePath,
        string directoryPath,
        int pageIndex,
        byte[] fileBytes)
    {
        var fileName = Path.GetFileName(filePath);
        var elements = new List<object>
        {
            new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"## 📄 文件预览\n会话: `{GetShortSessionLabel(sessionId)}`\n工作区: `{workspacePath}`\n文件: `{filePath}`\n大小: `{FormatFileSize(fileBytes.LongLength)}`"
                }
            },
            new { tag = "hr" }
        };

        var preview = TryBuildTextPreview(fileBytes);
        if (preview == null)
        {
            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = "当前文件不是可直接预览的文本文件，请在 Web 端查看完整内容。"
                }
            });
        }
        else
        {
            var notice = preview.Value.IsTruncated ? "\n\n_预览内容已截断_" : string.Empty;
            elements.Add(new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = $"### {fileName}\n```text\n{SanitizeCodeFence(preview.Value.Content)}\n```{notice}"
                }
            });
        }

        elements.Add(BuildActionButton(
            "📋 发送文件路径",
            "default",
            new
            {
                action = "copy_path_to_chat",
                chat_key = chatKey,
                copy_path = BuildWorkspaceEntryFullPath(workspacePath, filePath)
            }));

        elements.Add(BuildActionButton(
            "📂 返回目录",
            "default",
            new
            {
                action = "browse_session_directory",
                chat_key = chatKey,
                session_id = sessionId,
                directory_path = directoryPath,
                page = pageIndex
            }));

        elements.Add(BuildActionButton(
            "📋 返回会话管理",
            "primary",
            new
            {
                action = "open_session_manager"
            }));

        return new ElementsCardV2Dto
        {
            Header = new ElementsCardV2Dto.HeaderSuffix
            {
                Template = "turquoise",
                Title = new HeaderTitleElement { Content = "📄 文件预览" }
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
    }

    private List<SessionDirectoryEntry> GetDirectoryEntries(string workspacePath, string directoryPath)
    {
        var directories = Directory.GetDirectories(directoryPath)
            .Select(path => new DirectoryInfo(path))
            .Where(info => !info.Name.StartsWith(".", StringComparison.Ordinal))
            .Select(info => new SessionDirectoryEntry(
                info.Name,
                Path.GetRelativePath(workspacePath, info.FullName).Replace("\\", "/"),
                true,
                0,
                string.Empty))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

        var files = Directory.GetFiles(directoryPath)
            .Select(path => new FileInfo(path))
            .Where(info => !info.Name.StartsWith(".", StringComparison.Ordinal))
            .Where(info => !IsReservedDeviceName(info.Name))
            .Select(info => new SessionDirectoryEntry(
                info.Name,
                Path.GetRelativePath(workspacePath, info.FullName).Replace("\\", "/"),
                false,
                GetFileSizeSafely(info),
                info.Extension))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

        return directories.Concat(files).ToList();
    }

    private static long GetFileSizeSafely(FileInfo info)
    {
        try
        {
            return info.Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (NotSupportedException)
        {
            return 0;
        }
    }

    private static bool IsReservedDeviceName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return !string.IsNullOrWhiteSpace(baseName) && ReservedDeviceNames.Contains(baseName);
    }

    private object BuildSessionDirectoryEntryRow(
        SessionDirectoryEntry entry,
        string chatKey,
        string sessionId,
        string directoryPath,
        int pageIndex)
    {
        var icon = entry.IsDirectory ? "📁" : "📄";
        var meta = entry.IsDirectory
            ? "目录"
            : $"{FormatFileSize(entry.Size)}{(string.IsNullOrWhiteSpace(entry.Extension) ? string.Empty : $" · {entry.Extension}")}";

        object callbackValue = entry.IsDirectory
            ? new
            {
                action = "browse_session_directory",
                chat_key = chatKey,
                session_id = sessionId,
                directory_path = entry.RelativePath,
                page = 0
            }
            : new
            {
                action = "preview_session_file",
                chat_key = chatKey,
                session_id = sessionId,
                file_path = entry.RelativePath,
                directory_path = directoryPath,
                page = pageIndex
            };

        return new
        {
            tag = "column_set",
            flex_mode = "none",
            background_style = "default",
            columns = new object[]
            {
                new
                {
                    tag = "column",
                    width = "weighted",
                    weight = 5,
                    vertical_align = "top",
                    elements = new object[]
                    {
                        new
                        {
                            tag = "div",
                            text = new
                            {
                                tag = "lark_md",
                                content = $"**{icon} {entry.Name}**\n`{entry.RelativePath}`\n{meta}"
                            }
                        }
                    }
                },
                new
                {
                    tag = "column",
                    width = "auto",
                    vertical_align = "top",
                    elements = new object[]
                    {
                        new
                        {
                            tag = "button",
                            text = new { tag = "plain_text", content = "复制路径" },
                            type = "default",
                            behaviors = new[]
                            {
                                new
                                {
                                    type = "callback",
                                    value = new
                                    {
                                        action = "copy_path_to_chat",
                                        chat_key = chatKey,
                                        copy_path = BuildWorkspaceEntryFullPath(workspacePath: _cliExecutor.GetSessionWorkspacePath(sessionId), relativePath: entry.RelativePath)
                                    }
                                }
                            }
                        },
                        new
                        {
                            tag = "button",
                            text = new { tag = "plain_text", content = entry.IsDirectory ? "打开" : "查看" },
                            type = entry.IsDirectory ? "primary" : "default",
                            behaviors = new[]
                            {
                                new
                                {
                                    type = "callback",
                                    value = callbackValue
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private object BuildAllowedDirectoryRootRow(AllowedDirectoryRootItem root, string chatKey, string toolId)
    {
        return new
        {
            tag = "column_set",
            flex_mode = "none",
            background_style = "default",
            columns = new object[]
            {
                new
                {
                    tag = "column",
                    width = "weighted",
                    weight = 5,
                    vertical_align = "top",
                    elements = new object[]
                    {
                        new
                        {
                            tag = "div",
                            text = new
                            {
                                tag = "lark_md",
                                content = $"**📁 {root.Name}**\n`{root.Path}`"
                            }
                        }
                    }
                },
                new
                {
                    tag = "column",
                    width = "auto",
                    vertical_align = "top",
                    elements = new object[]
                    {
                        BuildActionButton(
                            "复制路径",
                            "default",
                            new
                            {
                                action = "copy_path_to_chat",
                                chat_key = chatKey,
                                copy_path = root.Path
                            }),
                        BuildActionButton(
                            "进入",
                            "primary",
                            new
                            {
                                action = "browse_allowed_directory",
                                chat_key = chatKey,
                                workspace_path = root.Path,
                                tool_id = toolId
                            })
                    }
                }
            }
        };
    }

    private object BuildAllowedDirectoryEntryRow(AllowedDirectoryBrowseEntry entry, string chatKey, string toolId)
    {
        var meta = entry.IsDirectory
            ? "目录"
            : $"{FormatFileSize(entry.Size)}{(string.IsNullOrWhiteSpace(entry.Extension) ? string.Empty : $" · {entry.Extension}")}";

        var actions = new List<object>
        {
            BuildActionButton(
                "复制路径",
                "default",
                new
                {
                    action = "copy_path_to_chat",
                    chat_key = chatKey,
                    copy_path = entry.Path
                })
        };

        if (entry.IsDirectory)
        {
            actions.Add(BuildActionButton(
                "进入",
                "primary",
                new
                {
                    action = "browse_allowed_directory",
                    chat_key = chatKey,
                    workspace_path = entry.Path,
                    tool_id = toolId
                }));
        }

        return new
        {
            tag = "column_set",
            flex_mode = "none",
            background_style = "default",
            columns = new object[]
            {
                new
                {
                    tag = "column",
                    width = "weighted",
                    weight = 5,
                    vertical_align = "top",
                    elements = new object[]
                    {
                        new
                        {
                            tag = "div",
                            text = new
                            {
                                tag = "lark_md",
                                content = $"**{(entry.IsDirectory ? "📁" : "📄")} {entry.Name}**\n`{entry.Path}`\n{meta}"
                            }
                        }
                    }
                },
                new
                {
                    tag = "column",
                    width = "auto",
                    vertical_align = "top",
                    elements = actions.ToArray()
                }
            }
        };
    }

    private static object BuildActionButton(string text, string type, object value)
    {
        return new
        {
            tag = "button",
            text = new { tag = "plain_text", content = text },
            type,
            behaviors = new[]
            {
                new
                {
                    type = "callback",
                    value
                }
            }
        };
    }

    private async Task SendElementsCardToChatAsync(string chatId, ElementsCardV2Dto card, string fallbackMessage, string? username = null, string? appId = null)
    {
        try
        {
            var cardJson = JsonSerializer.Serialize(card, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            var effectiveOptions = await ResolveEffectiveOptionsAsync(username, appId);
            await _cardKit.SendRawCardAsync(chatId, cardJson, optionsOverride: effectiveOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送飞书卡片失败: ChatId={ChatId}", chatId);
            await _feishuChannel.SendMessageAsync(chatId, fallbackMessage, username, appId);
        }
    }

    private static string BuildWorkspaceEntryFullPath(string workspacePath, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(
            workspacePath,
            relativePath.Replace("/", Path.DirectorySeparatorChar.ToString())));
    }

    private static string NormalizeWorkspaceRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path
            .Replace("\\", "/")
            .Trim()
            .Trim('/');

        return normalized == "." ? string.Empty : normalized;
    }

    private static string GetParentDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return string.Empty;
        }

        var segments = directoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length <= 1 ? string.Empty : string.Join("/", segments.Take(segments.Length - 1));
    }

    private static string GetDirectoryDisplayPath(string directoryPath)
    {
        return string.IsNullOrWhiteSpace(directoryPath) ? "/" : $"/{directoryPath}";
    }

    private static string GetShortSessionLabel(string sessionId)
    {
        return sessionId.Length <= 8 ? sessionId : $"{sessionId[..8]}...";
    }

    private static string ResolveWorkspaceFullPath(string workspacePath, string relativePath)
    {
        var fullPath = string.IsNullOrWhiteSpace(relativePath)
            ? Path.GetFullPath(workspacePath)
            : Path.GetFullPath(Path.Combine(workspacePath, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString())));

        if (!IsPathWithinRoot(workspacePath, fullPath))
        {
            throw new UnauthorizedAccessException($"超出工作区范围: {relativePath}");
        }

        return fullPath;
    }

    private static bool IsPathWithinRoot(string rootPath, string targetPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedTarget.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Content, bool IsTruncated)? TryBuildTextPreview(byte[] fileBytes)
    {
        if (fileBytes.Any(static b => b == 0))
        {
            return null;
        }

        var content = Encoding.UTF8.GetString(fileBytes);
        var normalizedContent = content.Replace("\r\n", "\n");
        var lines = normalizedContent.Split('\n');
        var previewLines = lines.Take(SessionFilePreviewLineLimit).ToList();
        var previewContent = string.Join("\n", previewLines);

        var isTruncated = lines.Length > SessionFilePreviewLineLimit || previewContent.Length > SessionFilePreviewCharacterLimit;
        if (previewContent.Length > SessionFilePreviewCharacterLimit)
        {
            previewContent = previewContent[..SessionFilePreviewCharacterLimit];
        }

        return (previewContent, isTruncated);
    }

    private static string SanitizeCodeFence(string content)
    {
        return content.Replace("```", "'''");
    }

    private static string FormatFileSize(long size)
    {
        var units = new[] { "B", "KB", "MB", "GB" };
        double value = size;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size} B" : $"{value:0.#} {units[unitIndex]}";
    }

    private sealed record SessionDirectoryEntry(
        string Name,
        string RelativePath,
        bool IsDirectory,
        long Size,
        string Extension);

    private sealed class ProjectServiceScopeContext(IServiceScope scope, IProjectService projectService, string username) : IDisposable
    {
        public IProjectService ProjectService { get; } = projectService;

        public string Username { get; } = username;

        public void Dispose()
        {
            scope.Dispose();
        }
    }

    private async Task<List<ChatSessionEntity>> GetChatSessionEntitiesAsync(string chatKey, string username)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();

        var sessions = await repo.GetByUsernameOrderByUpdatedAtAsync(username);
        return sessions
            .Where(s => string.Equals(s.FeishuChatKey, chatKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.UpdatedAt)
            .ToList();
    }

    private static string GetToolDisplayName(string? toolId)
    {
        return NormalizeToolId(toolId) switch
        {
            "claude-code" => "Claude Code",
            "codex" => "Codex",
            "opencode" => "OpenCode",
            _ => "未设置"
        };
    }
}
