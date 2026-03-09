using System.Collections.Concurrent;
using System.Text.Json;
using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书消息事件处理器
/// 处理 im.message.receive_v1 事件
/// 实现 FeishuNetSdk 的 IEventHandler 接口以自动接收事件
/// </summary>
public class FeishuMessageHandler : IEventHandler<EventV2Dto<ImMessageReceiveV1EventBodyDto>, ImMessageReceiveV1EventBodyDto>, IDisposable
{
    private readonly FeishuOptions _options;
    private readonly ILogger<FeishuMessageHandler> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly FeishuCommandService _commandService;
    private readonly FeishuHelpCardBuilder _cardBuilder;
    private readonly IFeishuCardKitClient _cardKit;
    private readonly ICliExecutorService _cliExecutor;
    private readonly IFeishuChannelService _feishuChannel;

    /// <summary>
    /// 静态消息收到事件（解决 SDK 创建不同实例的问题）
    /// </summary>
    public static event Func<FeishuIncomingMessage, Task>? MessageReceived;

    /// <summary>
    /// 已处理的消息 ID（去重）
    /// Key: MessageId, Value: 处理时间
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _processedMessages = new();

    /// <summary>
    /// 定时清理器（修复内存泄漏问题）
    /// </summary>
    private readonly System.Threading.Timer _cleanupTimer;
    private bool _disposed = false;

    public FeishuMessageHandler(
        IOptions<FeishuOptions> options,
        ILogger<FeishuMessageHandler> logger,
        IServiceProvider serviceProvider,
        FeishuCommandService commandService,
        FeishuHelpCardBuilder cardBuilder,
        IFeishuCardKitClient cardKit,
        ICliExecutorService cliExecutor,
        IFeishuChannelService feishuChannel)
    {
        _options = options.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _commandService = commandService;
        _cardBuilder = cardBuilder;
        _cardKit = cardKit;
        _cliExecutor = cliExecutor;
        _feishuChannel = feishuChannel;

        // 启动定时清理器（每 5 分钟清理一次过期消息）
        _cleanupTimer = new System.Threading.Timer(
            _ => CleanupOldMessages(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// 实现 IEventHandler 接口 - SDK 自动调用此方法
    /// </summary>
    /// <param name="input">飞书事件 DTO</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ExecuteAsync(EventV2Dto<ImMessageReceiveV1EventBodyDto> input, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔥 [Feishu] 收到事件: EventId={EventId}, EventType={EventType}", input.EventId, input.Header?.EventType);
        try
        {
            await HandleMessageReceiveAsync(input.Event);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [Feishu] 处理消息异常");
        }
    }

    /// <summary>
    /// 处理收到的消息事件（内部方法）
    /// </summary>
    /// <param name="eventDto">飞书事件 DTO</param>
    private async Task HandleMessageReceiveAsync(ImMessageReceiveV1EventBodyDto eventDto)
    {
        var message = eventDto.Message;
        _logger.LogInformation("🔥 [Feishu] 消息详情: ChatId={ChatId}, ChatType={ChatType}, MessageType={MessageType}, Content={Content}",
            message.ChatId, message.ChatType, message.MessageType, message.Content);

        // 去重检查 - 使用 MessageId 而非 EventId，原子操作避免并发重复处理
        if (!_processedMessages.TryAdd(message.MessageId, DateTime.UtcNow))
        {
            _logger.LogDebug("Duplicate message ignored: {MessageId}", message.MessageId);
            return;
        }

        // 清理过期的消息记录（超过 15 分钟的记录）
        CleanupOldMessages();

        // 解析消息内容
        var content = ParseMessageContent(message.Content, message.MessageType);

        // 检测 feishuhelp 命令（飞书会自动去掉斜杠，所以不检测斜杠）
        var trimmedContent = content.Trim();
        if (!string.IsNullOrEmpty(trimmedContent) &&
            (trimmedContent.StartsWith("feishuhelp", StringComparison.OrdinalIgnoreCase) ||
             trimmedContent.StartsWith("/feishuhelp", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("🔥 [Feishu] 检测到 feishuhelp 命令!");
            var keyword = trimmedContent.StartsWith("/", StringComparison.Ordinal)
                ? trimmedContent.Substring("/feishuhelp".Length).Trim()
                : trimmedContent.Substring("feishuhelp".Length).Trim();
            await HandleFeishuHelpAsync(message.ChatId, message.MessageId, keyword);
            return; // 系统命令处理完直接返回，不再触发事件
        }

        // 检测 feishusessions 命令
        if (!string.IsNullOrEmpty(trimmedContent) &&
            (trimmedContent.StartsWith("feishusessions", StringComparison.OrdinalIgnoreCase) ||
             trimmedContent.StartsWith("/feishusessions", StringComparison.OrdinalIgnoreCase) ||
             trimmedContent.StartsWith("feishusession", StringComparison.OrdinalIgnoreCase) ||
             trimmedContent.StartsWith("/feishusession", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("🔥 [Feishu] 检测到 feishusessions 命令!");
            await HandleSessionsCommandAsync(message.ChatId, message.MessageId);
            return; // 系统命令处理完直接返回，不再触发事件
        }

        // 获取发送者信息
        var senderId = eventDto.Sender.SenderId.OpenId ?? eventDto.Sender.SenderId.UserId ?? string.Empty;

        // 检测是否 @ 了机器人
        var isBotMentioned = CheckBotMention(message);

        // 群聊过滤：只有 @ 机器人才处理
        if (message.ChatType == "group" && !isBotMentioned)
        {
            _logger.LogDebug("Group message without @mention ignored: {ChatId}", message.ChatId);
            return;
        }

        // 移除 @ 提及占位符（Feishu 使用 @_user_1 风格的占位符）
        if (isBotMentioned && message.Mentions?.Length > 0)
        {
            foreach (var mention in message.Mentions)
            {
                // 使用正则表达式进行全局替换，处理可能的特殊字符
                var pattern = System.Text.RegularExpressions.Regex.Escape(mention.Key);
                content = System.Text.RegularExpressions.Regex.Replace(content, pattern, "").Trim();
            }
        }

        var incomingMessage = new FeishuIncomingMessage
        {
            MessageId = message.MessageId,
            ChatId = message.ChatId,
            ChatType = message.ChatType,
            Content = content,
            SenderId = senderId,
            SenderName = eventDto.Sender.SenderId.UnionId ?? senderId,
            ChatName = message.ChatType == "p2p" ? senderId : message.ChatId,
            IsBotMentioned = isBotMentioned,
            EventId = message.MessageId // 使用 MessageId 作为事件 ID 去重
        };

        _logger.LogInformation(
            "🔥 [Feishu] 消息处理完成: [{ChatType}] {ChatId} from {SenderId}: {Content}",
            message.ChatType, message.ChatId, senderId,
            content.Length > 50 ? $"{content[..50]}..." : content);

        // 转发消息给频道服务处理
        try
        {
            _logger.LogInformation("🔥 [Feishu] 转发消息给 FeishuChannelService 处理");
            await _feishuChannel.HandleIncomingMessageAsync(incomingMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [Feishu] 消息转发处理失败");
        }
    }

    /// <summary>
    /// 解析消息内容
    /// </summary>
    /// <param name="rawContent">原始 JSON 内容</param>
    /// <param name="msgType">消息类型</param>
    /// <returns>解析后的文本内容</returns>
    private static string ParseMessageContent(string rawContent, string msgType)
    {
        if (string.IsNullOrEmpty(rawContent))
            return string.Empty;

        try
        {
            var json = JsonDocument.Parse(rawContent);
            var root = json.RootElement;

            return msgType switch
            {
                "text" => root.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "",
                "post" => root.TryGetProperty("content", out var contentEl) ? contentEl.GetString() ?? "" : "",
                _ => rawContent
            };
        }
        catch
        {
            // JSON 解析失败，返回原始内容
            return rawContent;
        }
    }

    /// <summary>
    /// 检查是否 @ 了机器人
    /// </summary>
    /// <param name="message">消息对象</param>
    /// <returns>是否 @ 了机器人</returns>
    private bool CheckBotMention(ImMessageReceiveV1EventBodyDto.EventMessage message)
    {
        // P2P 消息始终视为需要响应
        if (message.ChatType == "p2p")
            return true;

        // 没有任何提及
        if (message.Mentions == null || message.Mentions.Length == 0)
            return false;

        // 检查是否有 @_all 或机器人的 open_id
        foreach (var mention in message.Mentions)
        {
            // @所有人
            if (mention.Key == "@_all")
                return true;

            // @机器人（通过 open_id 匹配）
            if (mention.Id?.OpenId == _options.AppId)
                return true;

            // @机器人（通过名称匹配 - 作为后备方案）
            if (mention.Id == null && mention.Name == _options.DefaultCardTitle)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 清理过期的消息记录
    /// 防止内存无限增长
    /// </summary>
    private void CleanupOldMessages()
    {
        var expireTime = DateTime.UtcNow.AddMinutes(-15);
        var keysToRemove = _processedMessages
            .Where(kvp => kvp.Value < expireTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _processedMessages.TryRemove(key, out _);
        }

        // 额外保护：如果记录数超过 500，删除最旧的记录
        if (_processedMessages.Count > 500)
        {
            var oldestKeys = _processedMessages
                .OrderBy(kvp => kvp.Value)
                .Take(100)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldestKeys)
            {
                _processedMessages.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// 处理 /feishuhelp 命令
    /// </summary>
    private async Task HandleFeishuHelpAsync(string chatId, string replyToMessageId, string keyword)
    {
        _logger.LogInformation("🔥 [FeishuHelp] 收到帮助请求: ChatId={ChatId}, ReplyToMessageId={ReplyToMessageId}, Keyword={Keyword}",
            chatId, replyToMessageId, keyword);

        try
        {
            // 自动刷新命令列表，确保获取最新的技能和插件
            _logger.LogInformation("🔥 [FeishuHelp] 开始刷新命令列表...");
            await _commandService.RefreshCommandsAsync();
            _logger.LogInformation("✅ [FeishuHelp] 命令列表刷新完成");

            List<FeishuCommandCategory> categories;
            string cardJson;

            _logger.LogInformation("🔥 [FeishuHelp] 开始获取命令列表...");
            if (string.IsNullOrEmpty(keyword))
            {
                categories = await _commandService.GetCategorizedCommandsAsync();
                _logger.LogInformation("🔥 [FeishuHelp] 获取到 {Count} 个分组", categories.Count);
                cardJson = _cardBuilder.BuildCommandListCard(categories);
            }
            else
            {
                categories = await _commandService.FilterCommandsAsync(keyword);
                _logger.LogInformation("🔥 [FeishuHelp] 过滤后获取到 {Count} 个分组", categories.Count);
                cardJson = _cardBuilder.BuildFilteredCard(categories, keyword);
            }

            _logger.LogInformation("🔥 [FeishuHelp] 卡片JSON长度: {Length}", cardJson.Length);
            _logger.LogDebug("🔥 [FeishuHelp] 卡片JSON内容: {CardJson}", cardJson);

            // 使用 CardKit 发送原始JSON卡片
            _logger.LogInformation("🔥 [FeishuHelp] 开始调用 ReplyRawCardAsync...");
            var messageId = await _cardKit.ReplyRawCardAsync(replyToMessageId, cardJson);
            _logger.LogInformation("✅ [FeishuHelp] 帮助卡片已发送, MessageId={MessageId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [FeishuHelp] 发送帮助卡片失败");

            // 降级到简单文本
            try
            {
                _logger.LogInformation("🔥 [FeishuHelp] 尝试发送降级提示...");
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "❌ [FeishuHelp] 发送降级提示也失败了");
            }
        }
    }

    /// <summary>
    /// 处理/sessions命令，返回会话管理卡片
    /// </summary>
    private async Task HandleSessionsCommandAsync(string chatId, string replyToMessageId)
    {
        try
        {
            // 直接使用chatId作为key（统一规则，去掉AppId前缀）
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

            //添加会话列表
            foreach (var sessionId in sessions.Take(10)) // 最多显示10个最近的会话
            {
                var workspacePath = _cliExecutor.GetSessionWorkspacePath(sessionId);
                var lastActiveTime = _feishuChannel.GetSessionLastActiveTime(sessionId);
                var isCurrent = sessionId == currentSessionId;

                var sessionInfo = $"{(isCurrent ? "✅ " : "")}**会话ID: {sessionId[..8]}...**\n📂 {workspacePath}\n⏱️ {lastActiveTime:yyyy-MM-dd HH:mm}";

                // 添加会话信息
                elements.Add(new
                {
                    tag = "div",
                    text = new { tag = "lark_md", content = sessionInfo }
                });

                // 添加会话操作按钮
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
            }

            if (sessions.Count == 0)
            {
                elements.Add(new
                {
                    tag = "div",
                    text = new { tag = "plain_text", content = "暂无会话，请点击下方「新建会话」按钮创建会话。" }
                });
            }

            elements.Add(new {
                tag = "hr"
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
                            action = "create_session",
                            chat_key = chatKey
                        }
                    }
                }
            });

            elements.Add(new
            {
                tag = "button",
                text = new { tag = "plain_text", content = "🧹 清理空闲会话" },
                type = "default",
                behaviors = new[]
                {
                    new
                    {
                        type = "callback",
                        value = new
                        {
                            action = "clean_idle_sessions",
                            chat_key = chatKey
                        }
                    }
                }
            });

            // 构建卡片（与帮助卡片格式完全一致）
            var card = new
            {
                schema = "2.0",
                config = new { enable_forward = true, update_multi = true },
                header = new { template = "blue", title = new { tag = "plain_text", content = "📋 会话管理" } },
                body = new { elements = elements.ToArray() }
            };

            // 发送卡片
            var cardJson = JsonSerializer.Serialize(card);
            var messageId = await _cardKit.ReplyRawCardAsync(replyToMessageId, cardJson);
            _logger.LogInformation("✅ [Feishu] 会话管理卡片已发送, MessageId={MessageId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理sessions命令失败");
            await _feishuChannel.ReplyMessageAsync(replyToMessageId, "❌ 会话管理功能暂时不可用，请稍后重试。");
        }
    }
}
