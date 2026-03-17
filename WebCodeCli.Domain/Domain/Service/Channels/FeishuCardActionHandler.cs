using System.Text.Json;
using System.Text.Json.Serialization;
using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Services;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书卡片动作事件处理器
/// 处理 card.action.trigger 事件（卡片按钮点击回调）
/// 使用 ICallbackHandler 接口支持在响应中直接返回卡片
/// </summary>
public class FeishuCardActionHandler : ICallbackHandler<
    CallbackV2Dto<CardActionTriggerEventBodyDto>,
    CardActionTriggerEventBodyDto,
    CardActionTriggerResponseDto>
{
    private readonly FeishuCardActionService _actionService;
    private readonly ILogger<FeishuCardActionHandler> _logger;

    static FeishuCardActionHandler()
    {
        Console.WriteLine("🔥🔥🔥 [FeishuCard] FeishuCardActionHandler (CallbackV2) 静态构造函数被调用！");
    }

    public FeishuCardActionHandler(
        FeishuCardActionService actionService,
        ILogger<FeishuCardActionHandler> logger)
    {
        _actionService = actionService;
        _logger = logger;

        _logger.LogInformation("🔥 [FeishuCard] FeishuCardActionHandler (CallbackV2) 已创建");
    }

    /// <summary>
    /// 实现 ICallbackHandler 接口 - SDK 自动调用此方法
    /// 支持在返回值中直接包含 toast 和 card
    /// </summary>
    public async Task<CardActionTriggerResponseDto> ExecuteAsync(
        CallbackV2Dto<CardActionTriggerEventBodyDto> input,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔥 [FeishuCard] 收到卡片回调事件 (CallbackV2): EventId={EventId}, EventType={EventType}",
            input.EventId, input.Header?.EventType);

        try
        {
            var response = await HandleCardActionTriggerAsync(input.Event);

            // 记录返回的响应内容
            var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            _logger.LogInformation("🔥 [FeishuCard] 返回回调响应: {ResponseJson}", responseJson);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [FeishuCard] 处理卡片回调异常");
            return new CardActionTriggerResponseDto();
        }
    }

    /// <summary>
    /// 处理卡片动作触发事件
    /// </summary>
    private async Task<CardActionTriggerResponseDto> HandleCardActionTriggerAsync(CardActionTriggerEventBodyDto eventDto)
    {
        try
        {
            _logger.LogInformation("🔥 [FeishuCard] 卡片回调详情: Token={Token}, OperatorId={OperatorId}",
                eventDto.Token, eventDto.Operator?.OpenId);

            // 记录 Context 的完整内容
            if (eventDto.Context != null)
            {
                var contextJson = JsonSerializer.Serialize(eventDto.Context);
                _logger.LogInformation("🔥 [FeishuCard] Context 内容: {ContextJson}", contextJson);
            }
            else
            {
                _logger.LogWarning("🔥 [FeishuCard] Context 为空");
            }

            // 记录 Action 的完整内容
            if (eventDto.Action != null)
            {
                var actionJson = JsonSerializer.Serialize(eventDto.Action);
                _logger.LogInformation("🔥 [FeishuCard] Action 内容: {ActionJson}", actionJson);
            }
            else
            {
                _logger.LogWarning("🔥 [FeishuCard] Action 为空");
                return new CardActionTriggerResponseDto();
            }

            // 获取 ChatId（用于执行命令时）
            var chatId = eventDto.Context?.OpenChatId;
            _logger.LogInformation("🔥 [FeishuCard] OpenChatId: {ChatId}", chatId ?? "null");

            // 处理输入框事件
            if (eventDto.Action.Tag == "input" && !string.IsNullOrEmpty(eventDto.Action.Name))
            {
                var inputValue = eventDto.Action.InputValue;
                _logger.LogInformation("🔥 [FeishuCard] 输入框事件: Name={Name}, InputValue={InputValue}", 
                    eventDto.Action.Name, inputValue);
            }

            // 解析 action.value 或 action.option
            string? actionValue = null;
            if (eventDto.Action.Value != null)
            {
                try
                {
                    // Value 是 Dictionary<string, object> 类型，直接序列化为 JSON
                    actionValue = JsonSerializer.Serialize(eventDto.Action.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🔥 [FeishuCard] 解析 action.value 失败");
                    // 尝试直接使用原始值
                    actionValue = eventDto.Action.Value?.ToString();
                }
            }
            else if (!string.IsNullOrEmpty(eventDto.Action.Option))
            {
                actionValue = eventDto.Action.Option;
            }

            if (string.IsNullOrEmpty(actionValue) && eventDto.Action.Tag == "button" && !string.IsNullOrWhiteSpace(eventDto.Action.Name))
            {
                actionValue = BuildFormSubmitActionValue(eventDto.Action.Name);
                if (!string.IsNullOrEmpty(actionValue))
                {
                    _logger.LogInformation("🔥 [FeishuCard] 根据按钮名称回填 Action.Value: Name={ActionName}, ActionValue={ActionValue}",
                        eventDto.Action.Name,
                        actionValue);
                }
            }

            if (string.IsNullOrEmpty(actionValue))
            {
                _logger.LogWarning("🔥 [FeishuCard] 空的 action.value 和 action.option");
                return new CardActionTriggerResponseDto();
            }

            _logger.LogInformation("🔥 [FeishuCard] Action.Value: {ActionValue}",
                actionValue.Length > 200 ? actionValue[..200] + "..." : actionValue);

            // 记录 FormValue
            if (eventDto.Action.FormValue != null)
            {
                var formValueJson = JsonSerializer.Serialize(eventDto.Action.FormValue);
                _logger.LogInformation("🔥 [FeishuCard] FormValue 内容: {FormValueJson}", formValueJson);
            }
            else
            {
                _logger.LogWarning("🔥 [FeishuCard] FormValue 为空");
            }

            // 调用服务处理并返回响应对象
            return await _actionService.HandleCardActionAsync(
                actionValue,
                eventDto.Action.FormValue,
                chatId,
                eventDto.Action.InputValue,
                eventDto.Operator?.UnionId ?? eventDto.Operator?.OpenId ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [FeishuCard] 处理卡片回调失败");
            return new CardActionTriggerResponseDto();
        }
    }

    private static string? BuildFormSubmitActionValue(string actionName)
    {
        if (string.Equals(actionName, "bind_web_user_submit", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new { action = "bind_web_user" });
        }

        if (string.Equals(actionName, "create_project_submit", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new { action = "create_project" });
        }

        if (string.Equals(actionName, "fetch_project_branches_submit", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new { action = "fetch_project_branches" });
        }

        if (actionName.StartsWith("update_project_submit__", StringComparison.Ordinal))
        {
            var projectId = actionName["update_project_submit__".Length..];
            return JsonSerializer.Serialize(new
            {
                action = "update_project",
                project_id = projectId
            });
        }

        if (actionName.StartsWith("fetch_project_branches_submit__", StringComparison.Ordinal))
        {
            var projectId = actionName["fetch_project_branches_submit__".Length..];
            return JsonSerializer.Serialize(new
            {
                action = "fetch_project_branches",
                project_id = projectId
            });
        }

        return null;
    }
}
