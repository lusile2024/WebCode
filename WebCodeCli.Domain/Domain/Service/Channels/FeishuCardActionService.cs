using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;
using Microsoft.Extensions.Logging;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书卡片回调处理服务
/// 处理卡片按钮点击的回调逻辑
/// </summary>
public class FeishuCardActionService
{
    private readonly FeishuCommandService _commandService;
    private readonly FeishuHelpCardBuilder _cardBuilder;
    private readonly ILogger<FeishuCardActionService> _logger;

    public FeishuCardActionService(
        FeishuCommandService commandService,
        FeishuHelpCardBuilder cardBuilder,
        ILogger<FeishuCardActionService> logger)
    {
        _commandService = commandService;
        _cardBuilder = cardBuilder;
        _logger = logger;
    }

    /// <summary>
    /// 处理卡片回调动作（核心逻辑）
    /// </summary>
    /// <param name="actionJson">action.value JSON字符串</param>
    /// <param name="formValue">form_value（execute_command时使用）</param>
    /// <param name="chatId">聊天ID（用于执行命令时创建会话）</param>
    /// <returns>回调响应对象（包含toast和card）</returns>
    public async Task<object?> HandleCardActionAsync(
        string actionJson,
        JsonElement? formValue = null,
        string? chatId = null)
    {
        try
        {
            _logger.LogInformation("🔥 [FeishuHelp] 收到卡片回调: ActionJson={ActionJson}",
                actionJson.Length > 200 ? actionJson[..200] + "..." : actionJson);

            var action = JsonSerializer.Deserialize<FeishuHelpCardAction>(actionJson);
            if (action == null)
            {
                _logger.LogWarning("🔥 [FeishuHelp] 无法解析 action: {ActionJson}", actionJson);
                return null;
            }

            _logger.LogInformation("🔥 [FeishuHelp] 卡片回调: Action={Action}, CommandId={CommandId}",
                action.Action, action.CommandId);

            return action.Action switch
            {
                "refresh_commands" => await HandleRefreshCommandsAsync(),
                "select_command" => await HandleSelectCommandAsync(action.CommandId),
                "back_to_list" => await HandleBackToListAsync(),
                "execute_command" => await HandleExecuteCommandAsync(formValue, chatId),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [FeishuHelp] 处理卡片回调失败");
            return _cardBuilder.BuildToastOnlyResponse("处理失败，请重试", "error");
        }
    }

    /// <summary>
    /// 处理刷新命令
    /// </summary>
    private async Task<object> HandleRefreshCommandsAsync()
    {
        await _commandService.RefreshCommandsAsync();
        var categories = await _commandService.GetCategorizedCommandsAsync();
        var cardJson = _cardBuilder.BuildCommandListCard(categories);
        return _cardBuilder.BuildToastResponse(cardJson, "✅ 命令列表已更新", "success");
    }

    /// <summary>
    /// 处理选择命令
    /// </summary>
    private async Task<object?> HandleSelectCommandAsync(string? commandId)
    {
        if (string.IsNullOrEmpty(commandId))
            return null;

        var command = await _commandService.GetCommandAsync(commandId);
        if (command == null)
        {
            var categories = await _commandService.GetCategorizedCommandsAsync();
            var cardJson = _cardBuilder.BuildCommandListCard(categories);
            return _cardBuilder.BuildToastResponse(cardJson, "❌ 命令不存在", "error");
        }

        var executeCardJson = _cardBuilder.BuildExecuteCard(command);
        return new
        {
            card = new
            {
                type = "raw",
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(executeCardJson)
            }
        };
    }

    /// <summary>
    /// 处理返回列表
    /// </summary>
    private async Task<object> HandleBackToListAsync()
    {
        var categories = await _commandService.GetCategorizedCommandsAsync();
        var cardJson = _cardBuilder.BuildCommandListCard(categories);
        return new
        {
            card = new
            {
                type = "raw",
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(cardJson)
            }
        };
    }

    /// <summary>
    /// 处理执行命令
    /// </summary>
    private async Task<object?> HandleExecuteCommandAsync(JsonElement? formValue, string? chatId)
    {
        // 从 form_value 获取命令输入
        if (formValue == null)
            return null;

        // 解析命令输入
        var commandInput = formValue.Value.TryGetProperty("command_input", out var inputEl)
            ? inputEl.GetString()
            : null;

        if (string.IsNullOrEmpty(commandInput))
        {
            return _cardBuilder.BuildToastOnlyResponse("请输入命令", "warning");
        }

        _logger.LogInformation("🚀 [FeishuHelp] 执行命令: {Command}", commandInput);

        // 注意：完整的执行流程需要与 FeishuChannelService 集成
        // 1. 获取 ChatId/Session
        // 2. 调用 CliExecutorService
        // 3. 使用 FeishuStreamingHandle 流式输出
        // 这里先返回 toast 确认，具体执行逻辑需要进一步集成

        return _cardBuilder.BuildToastOnlyResponse("🚀 开始执行命令...", "info");
    }

    /// <summary>
    /// 解析卡片回调响应并更新卡片
    /// 这个方法用于将 HandleCardActionAsync 的返回值转换为飞书SDK需要的格式
    /// </summary>
    public object? BuildCardActionResponse(object? actionResult)
    {
        // 直接返回 actionResult，飞书SDK应该能够处理
        return actionResult;
    }
}
