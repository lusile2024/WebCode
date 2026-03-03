
using System.Text.Json;
using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Services;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书卡片动作事件处理器
/// 处理 card.action.trigger 事件（卡片按钮点击回调）
/// </summary>
public class FeishuCardActionHandler : IEventHandler<EventV2Dto<CardActionTriggerEventBodyDto>, CardActionTriggerEventBodyDto>
{
    private readonly FeishuCardActionService _actionService;
    private readonly ILogger<FeishuCardActionHandler> _logger;

    static FeishuCardActionHandler()
    {
        Console.WriteLine("🔥🔥🔥 [FeishuCard] FeishuCardActionHandler 静态构造函数被调用！");
    }

    public FeishuCardActionHandler(
        FeishuCardActionService actionService,
        ILogger<FeishuCardActionHandler> logger)
    {
        _actionService = actionService;
        _logger = logger;

        _logger.LogInformation("🔥 [FeishuCard] FeishuCardActionHandler 已创建");
    }

    /// <summary>
    /// 实现 IEventHandler 接口 - SDK 自动调用此方法
    /// </summary>
    public async Task ExecuteAsync(EventV2Dto<CardActionTriggerEventBodyDto> input, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔥 [FeishuCard] 收到卡片回调事件: EventId={EventId}, EventType={EventType}",
            input.EventId, input.Header?.EventType);

        try
        {
            await HandleCardActionTriggerAsync(input.Event);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [FeishuCard] 处理卡片回调异常");
        }
    }

    /// <summary>
    /// 处理卡片动作触发事件
    /// </summary>
    private async Task HandleCardActionTriggerAsync(CardActionTriggerEventBodyDto eventDto)
    {
        try
        {
            _logger.LogInformation("🔥 [FeishuCard] 卡片回调详情: Token={Token}, OperatorId={OperatorId}",
                eventDto.Token, eventDto.Operator?.OpenId);

            // 解析 action.value
            var actionValue = eventDto.Action?.Value?.ToString();
            if (string.IsNullOrEmpty(actionValue))
            {
                _logger.LogWarning("🔥 [FeishuCard] 空的 action.value");
                return;
            }

            _logger.LogInformation("🔥 [FeishuCard] Action.Value: {ActionValue}",
                actionValue.Length > 200 ? actionValue[..200] + "..." : actionValue);

            // 获取 ChatId（用于执行命令时）
            var chatId = eventDto.Context?.OpenChatId;

            // 调用服务处理
            await _actionService.HandleCardActionAsync(
                actionValue,
                eventDto.Action?.FormValue,
                chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [FeishuCard] 处理卡片回调失败");
        }
    }
}

