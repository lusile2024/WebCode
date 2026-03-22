# 飞书渠道快速回复实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 实现飞书机器人快速回复功能，用户发送消息后 500ms 内看到"思考中"卡片，AI 处理过程中流式更新内容。

**Architecture:** 使用 FeishuNetSdk.WebSocket 接收消息，自封装 CardKitClient 实现流式卡片更新，与现有 CliExecutorService 集成。

**Tech Stack:** C# / .NET 8, FeishuNetSdk, HttpClient, Blazor Server

---

## Phase 1: 基础设施

### Task 1.1: 创建配置项类

**Files:**
- Create: `WebCodeCli.Domain/Common/Options/FeishuOptions.cs`

**Step 1: 创建配置类**

```csharp
namespace WebCodeCli.Domain.Common.Options;

/// <summary>
/// 飞书渠道配置选项
/// </summary>
public class FeishuOptions
{
    /// <summary>
    /// 是否启用飞书渠道
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 应用 ID
    /// </summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// 应用密钥
    /// </summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// 加密密钥（用于解密事件推送）
    /// </summary>
    public string EncryptKey { get; set; } = string.Empty;

    /// <summary>
    /// 验证令牌
    /// </summary>
    public string VerificationToken { get; set; } = string.Empty;

    /// <summary>
    /// 流式更新节流间隔（毫秒）
    /// </summary>
    public int StreamingThrottleMs { get; set; } = 500;

    /// <summary>
    /// 默认卡片标题
    /// </summary>
    public string DefaultCardTitle { get; set; } = "AI 助手";

    /// <summary>
    /// 思考中提示消息
    /// </summary>
    public string ThinkingMessage { get; set; } = "⏳ 思考中...";
}
```

**Step 2: 验证文件创建**

Run: `ls WebCodeCli.Domain/Common/Options/FeishuOptions.cs`
Expected: 文件存在

**Step 3: Commit**

```bash
git add WebCodeCli.Domain/Common/Options/FeishuOptions.cs
git commit -m "feat(feishu): 添加飞书渠道配置选项类"
```

---

### Task 1.2: 创建消息模型类

**Files:**
- Create: `WebCodeCli.Domain/Domain/Model/Channels/FeishuIncomingMessage.cs`
- Create: `WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingHandle.cs`

**Step 1: 创建目录**

Run: `mkdir -p WebCodeCli.Domain/Domain/Model/Channels`
Expected: 目录创建成功

**Step 2: 创建 FeishuIncomingMessage**

```csharp
namespace WebCodeCli.Domain.Domain.Model.Channels;

/// <summary>
/// 飞书收到的消息
/// </summary>
public class FeishuIncomingMessage
{
    /// <summary>
    /// 消息 ID
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// 会话 ID
    /// </summary>
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// 会话类型 (p2p/group)
    /// </summary>
    public string ChatType { get; set; } = "p2p";

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 发送者 ID
    /// </summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>
    /// 发送者名称
    /// </summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// 会话名称
    /// </summary>
    public string? ChatName { get; set; }

    /// <summary>
    /// 是否 @ 了机器人
    /// </summary>
    public bool IsBotMentioned { get; set; }

    /// <summary>
    /// 事件 ID（用于去重）
    /// </summary>
    public string EventId { get; set; } = string.Empty;
}
```

**Step 3: 创建 FeishuStreamingHandle**

```csharp
namespace WebCodeCli.Domain.Domain.Model.Channels;

/// <summary>
/// 飞书流式回复句柄
/// </summary>
public class FeishuStreamingHandle : IDisposable
{
    private readonly Func<string, Task> _updateAsync;
    private readonly Func<string, Task> _finishAsync;
    private readonly int _throttleMs;
    private DateTime _lastUpdate = DateTime.MinValue;
    private int _sequence = 0;
    private bool _disposed = false;

    /// <summary>
    /// 卡片 ID
    /// </summary>
    public string CardId { get; }

    /// <summary>
    /// 消息 ID
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    /// 当前序列号
    /// </summary>
    public int Sequence => _sequence;

    public FeishuStreamingHandle(
        string cardId,
        string messageId,
        Func<string, Task> updateAsync,
        Func<string, Task> finishAsync,
        int throttleMs = 500)
    {
        CardId = cardId;
        MessageId = messageId;
        _updateAsync = updateAsync;
        _finishAsync = finishAsync;
        _throttleMs = throttleMs;
    }

    /// <summary>
    /// 更新卡片内容（带节流）
    /// </summary>
    public async Task UpdateAsync(string content)
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        if ((now - _lastUpdate).TotalMilliseconds < _throttleMs)
            return; // 节流跳过

        _lastUpdate = now;
        _sequence++;
        await _updateAsync(content);
    }

    /// <summary>
    /// 完成更新
    /// </summary>
    public async Task FinishAsync(string finalContent)
    {
        if (_disposed) return;

        _sequence++;
        await _finishAsync(finalContent);
        _disposed = true;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
```

**Step 4: 验证文件创建**

Run: `ls WebCodeCli.Domain/Domain/Model/Channels/`
Expected: 两个文件都存在

**Step 5: Commit**

```bash
git add WebCodeCli.Domain/Domain/Model/Channels/
git commit -m "feat(feishu): 添加飞书消息模型和流式句柄"
```

---

### Task 1.3: 添加 NuGet 依赖

**Files:**
- Modify: `WebCodeCli.Domain/WebCodeCli.Domain.csproj`

**Step 1: 添加 FeishuNetSdk 包引用**

在 `<ItemGroup>` 中添加：

```xml
<PackageReference Include="FeishuNetSdk" Version="3.*" />
<PackageReference Include="FeishuNetSdk.WebSocket" Version="1.*" />
<PackageReference Include="FeishuNetSdk.Endpoint" Version="1.*" />
```

**Step 2: 还原依赖**

Run: `cd WebCodeCli.Domain && dotnet restore`
Expected: 依赖还原成功

**Step 3: Commit**

```bash
git add WebCodeCli.Domain/WebCodeCli.Domain.csproj
git commit -m "feat(feishu): 添加 FeishuNetSdk NuGet 依赖"
```

---

## Phase 2: CardKit 客户端

### Task 2.1: 创建 CardKit 客户端接口

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/Channels/IFeishuCardKitClient.cs`

**Step 1: 创建目录**

Run: `mkdir -p WebCodeCli.Domain/Domain/Service/Channels`
Expected: 目录创建成功

**Step 2: 创建接口**

```csharp
namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书 CardKit 客户端接口
/// </summary>
public interface IFeishuCardKitClient
{
    /// <summary>
    /// 创建流式卡片
    /// </summary>
    /// <param name="initialContent">初始内容</param>
    /// <param name="title">卡片标题</param>
    /// <returns>卡片 ID</returns>
    Task<string> CreateCardAsync(string initialContent, string title);

    /// <summary>
    /// 更新卡片内容
    /// </summary>
    /// <param name="cardId">卡片 ID</param>
    /// <param name="content">新内容</param>
    /// <param name="sequence">序列号（必须递增）</param>
    /// <returns>是否成功</returns>
    Task<bool> UpdateCardAsync(string cardId, string content, int sequence);

    /// <summary>
    /// 发送卡片消息到会话
    /// </summary>
    /// <param name="chatId">会话 ID</param>
    /// <param name="cardId">卡片 ID</param>
    /// <returns>消息 ID</returns>
    Task<string> SendCardMessageAsync(string chatId, string cardId);

    /// <summary>
    /// 回复消息（使用卡片）
    /// </summary>
    /// <param name="replyToMessageId">要回复的消息 ID</param>
    /// <param name="cardId">卡片 ID</param>
    /// <returns>消息 ID</returns>
    Task<string> ReplyCardMessageAsync(string replyToMessageId, string cardId);
}
```

**Step 3: Commit**

```bash
git add WebCodeCli.Domain/Domain/Service/Channels/IFeishuCardKitClient.cs
git commit -m "feat(feishu): 添加 CardKit 客户端接口"
```

---

### Task 2.2: 实现 CardKit 客户端

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardKitClient.cs`

**Step 1: 创建实现类**

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书 CardKit 客户端实现
/// 参考: https://open.feishu.cn/document/server-docs/cardkit-v1/card/create
/// </summary>
public class FeishuCardKitClient : IFeishuCardKitClient
{
    private readonly HttpClient _http;
    private readonly FeishuOptions _options;
    private readonly ILogger<FeishuCardKitClient> _logger;

    private const string BaseUrl = "https://open.feishu.cn/open-apis";
    private string? _accessToken;
    private DateTime _tokenExpiresAt;

    public FeishuCardKitClient(
        IOptions<FeishuOptions> options,
        ILogger<FeishuCardKitClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = new HttpClient();
    }

    /// <summary>
    /// 获取或刷新 TenantAccessToken
    /// </summary>
    private async Task<string> EnsureTokenAsync()
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiresAt)
            return _accessToken;

        var requestBody = new
        {
            app_id = _options.AppId,
            app_secret = _options.AppSecret
        };

        var response = await _http.PostAsJsonAsync(
            "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal",
            requestBody);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "Unknown error";
            throw new Exception($"Feishu auth failed: {msg}");
        }

        _accessToken = root.GetProperty("tenant_access_token").GetString();
        var expire = root.GetProperty("expire").GetInt32();
        _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expire - 60); // 提前 60 秒刷新

        _logger.LogDebug("Feishu token refreshed, expires at {ExpireAt}", _tokenExpiresAt);
        return _accessToken!;
    }

    /// <summary>
    /// 构建请求头
    /// </summary>
    private async Task<Dictionary<string, string>> BuildHeadersAsync()
    {
        var token = await EnsureTokenAsync();
        return new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}",
            ["Content-Type"] = "application/json; charset=utf-8"
        };
    }

    /// <summary>
    /// 构建卡片数据
    /// </summary>
    private static object BuildCardData(string content, string title, bool streaming = true)
    {
        return new
        {
            schema = "2.0",
            config = new
            {
                update_multi = true,
                streaming_mode = streaming
            },
            header = new
            {
                title = new { tag = "plain_text", content = title }
            },
            body = new
            {
                elements = new[]
                {
                    new { tag = "markdown", content }
                }
            }
        };
    }

    public async Task<string> CreateCardAsync(string initialContent, string title)
    {
        var headers = await BuildHeadersAsync();
        var cardData = BuildCardData(initialContent, title);

        var requestBody = new
        {
            type = "card_json",
            data = JsonSerializer.Serialize(cardData)
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/cardkit/v1/cards")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "Unknown error";
            _logger.LogError("CreateCard failed: {Message}", msg);
            throw new Exception($"CreateCard failed: {msg}");
        }

        var cardId = root.GetProperty("data").GetProperty("card_id").GetString()!;
        _logger.LogDebug("Card created: {CardId}", cardId);
        return cardId;
    }

    public async Task<bool> UpdateCardAsync(string cardId, string content, int sequence)
    {
        var headers = await BuildHeadersAsync();
        var cardData = BuildCardData(content, _options.DefaultCardTitle);

        var requestBody = new
        {
            card = new
            {
                type = "card_json",
                data = JsonSerializer.Serialize(cardData)
            },
            sequence
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/cardkit/v1/cards/{cardId}")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "Unknown error";
            _logger.LogWarning("UpdateCard failed (seq={Sequence}): {Message}", sequence, msg);
            return false;
        }

        _logger.LogDebug("Card updated: {CardId}, seq={Sequence}", cardId, sequence);
        return true;
    }

    public async Task<string> SendCardMessageAsync(string chatId, string cardId)
    {
        var headers = await BuildHeadersAsync();

        var requestBody = new
        {
            receive_id = chatId,
            msg_type = "interactive",
            content = JsonSerializer.Serialize(new
            {
                type = "card",
                data = new { card_id = cardId }
            })
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/im/v1/messages?receive_id_type=chat_id")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "Unknown error";
            throw new Exception($"SendCardMessage failed: {msg}");
        }

        var messageId = root.GetProperty("data").GetProperty("message_id").GetString()!;
        _logger.LogDebug("Card message sent to chat {ChatId}: {MessageId}", chatId, messageId);
        return messageId;
    }

    public async Task<string> ReplyCardMessageAsync(string replyToMessageId, string cardId)
    {
        var headers = await BuildHeadersAsync();

        var requestBody = new
        {
            msg_type = "interactive",
            content = JsonSerializer.Serialize(new
            {
                type = "card",
                data = new { card_id = cardId }
            })
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/im/v1/messages/{replyToMessageId}/reply")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "Unknown error";
            throw new Exception($"ReplyCardMessage failed: {msg}");
        }

        var messageId = root.GetProperty("data").GetProperty("message_id").GetString()!;
        _logger.LogDebug("Card reply sent: {MessageId}", messageId);
        return messageId;
    }
}
```

**Step 2: 验证编译**

Run: `cd WebCodeCli.Domain && dotnet build --no-restore`
Expected: 编译成功，无错误

**Step 3: Commit**

```bash
git add WebCodeCli.Domain/Domain/Service/Channels/FeishuCardKitClient.cs
git commit -m "feat(feishu): 实现 CardKit 客户端（流式卡片支持）"
```

---

## Phase 3: 核心服务

### Task 3.1: 创建主服务接口

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/Channels/IFeishuChannelService.cs`

**Step 1: 创建接口**

```csharp
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书渠道服务接口
/// </summary>
public interface IFeishuChannelService
{
    /// <summary>
    /// 服务是否运行中
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 发送文本消息
    /// </summary>
    Task<string> SendMessageAsync(string chatId, string content);

    /// <summary>
    /// 回复消息
    /// </summary>
    Task<string> ReplyMessageAsync(string messageId, string content);

    /// <summary>
    /// 发送流式消息（核心方法）
    /// </summary>
    /// <param name="chatId">会话 ID</param>
    /// <param name="initialContent">初始内容</param>
    /// <param name="replyToMessageId">要回复的消息 ID（可选）</param>
    /// <returns>流式句柄</returns>
    Task<FeishuStreamingHandle> SendStreamingMessageAsync(
        string chatId,
        string initialContent,
        string? replyToMessageId = null);
}
```

**Step 2: Commit**

```bash
git add WebCodeCli.Domain/Domain/Service/Channels/IFeishuChannelService.cs
git commit -m "feat(feishu): 添加飞书渠道服务接口"
```

---

### Task 3.2: 实现消息处理器（事件处理）

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/Channels/FeishuMessageHandler.cs`

**Step 1: 创建消息处理器**

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using FeishuNetSdk.Events.EventV2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书消息事件处理器
/// 处理 im.message.receive_v1 事件
/// </summary>
public class FeishuMessageHandler
{
    private readonly FeishuOptions _options;
    private readonly ILogger<FeishuMessageHandler> _logger;
    private readonly IFeishuChannelService _channelService;

    /// <summary>
    /// 已处理的消息 ID（去重）
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _processedMessages = new();

    /// <summary>
    /// 消息收到事件
    /// </summary>
    public event Func<FeishuIncomingMessage, Task>? MessageReceived;

    public FeishuMessageHandler(
        IOptions<FeishuOptions> options,
        ILogger<FeishuMessageHandler> logger,
        IFeishuChannelService channelService)
    {
        _options = options.Value;
        _logger = logger;
        _channelService = channelService;
    }

    /// <summary>
    /// 处理收到的消息事件
    /// </summary>
    public async Task HandleMessageReceiveAsync(EventV2Dto<ImMessageReceiveV1EventBodyDto> eventDto)
    {
        var body = eventDto.Event;
        var message = body.Message;

        // 去重检查
        if (_processedMessages.ContainsKey(message.MessageId))
        {
            _logger.LogDebug("Duplicate message ignored: {MessageId}", message.MessageId);
            return;
        }

        _processedMessages.TryAdd(message.MessageId, DateTime.UtcNow);
        CleanupOldMessages();

        // 解析消息内容
        var content = ParseMessageContent(message.Content, message.MessageType);

        // 获取发送者信息
        var senderId = body.Sender.SenderId.OpenId ?? body.Sender.SenderId.UserId ?? string.Empty;

        // 检测是否 @ 了机器人
        var isBotMentioned = CheckBotMention(message);

        // 群聊过滤：只有 @ 机器人才处理
        if (message.ChatType == "group" && !isBotMentioned)
        {
            _logger.LogDebug("Group message without @mention ignored: {ChatId}", message.ChatId);
            return;
        }

        // 移除 @ 提及
        if (isBotMentioned && message.Mentions?.Length > 0)
        {
            foreach (var mention in message.Mentions)
            {
                content = content.Replace(mention.Key, "").Trim();
            }
        }

        var incomingMessage = new FeishuIncomingMessage
        {
            MessageId = message.MessageId,
            ChatId = message.ChatId,
            ChatType = message.ChatType,
            Content = content,
            SenderId = senderId,
            SenderName = body.Sender.SenderId.UnionId ?? senderId,
            ChatName = message.ChatType == "p2p" ? senderId : message.ChatId,
            IsBotMentioned = isBotMentioned,
            EventId = eventDto.EventId
        };

        _logger.LogInformation(
            "Feishu message received: [{ChatType}] {ChatId} from {SenderId}: {Content}",
            message.ChatType, message.ChatId, senderId, content[..Math.Min(50, content.Length)]);

        // 触发事件
        if (MessageReceived != null)
        {
            await MessageReceived.Invoke(incomingMessage);
        }
    }

    /// <summary>
    /// 解析消息内容
    /// </summary>
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
            return rawContent;
        }
    }

    /// <summary>
    /// 检查是否 @ 了机器人
    /// </summary>
    private bool CheckBotMention(ImMessageReceiveV1EventBodyDto.MessageDto message)
    {
        if (message.ChatType == "p2p")
            return true;

        if (message.Mentions == null || message.Mentions.Length == 0)
            return false;

        // 检查是否有 @_user_ 或机器人的 open_id
        return message.Mentions.Any(m =>
            m.Key == "@_all" ||
            m.Id?.OpenId == _options.AppId);
    }

    /// <summary>
    /// 清理过期的消息记录
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
    }
}
```

**Step 2: 验证编译**

Run: `cd WebCodeCli.Domain && dotnet build --no-restore`
Expected: 编译成功

**Step 3: Commit**

```bash
git add WebCodeCli.Domain/Domain/Service/Channels/FeishuMessageHandler.cs
git commit -m "feat(feishu): 实现飞书消息事件处理器"
```

---

### Task 3.3: 实现主服务

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`

**Step 1: 创建主服务实现**

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书渠道服务实现
/// </summary>
public class FeishuChannelService : BackgroundService, IFeishuChannelService
{
    private readonly FeishuOptions _options;
    private readonly ILogger<FeishuChannelService> _logger;
    private readonly IFeishuCardKitClient _cardKit;
    private readonly FeishuMessageHandler _messageHandler;

    private bool _isRunning = false;

    public bool IsRunning => _isRunning;

    public FeishuChannelService(
        IOptions<FeishuOptions> options,
        ILogger<FeishuChannelService> logger,
        IFeishuCardKitClient cardKit,
        FeishuMessageHandler messageHandler)
    {
        _options = options.Value;
        _logger = logger;
        _cardKit = cardKit;
        _messageHandler = messageHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Feishu channel is disabled");
            return;
        }

        _logger.LogInformation("Starting Feishu channel service...");

        // 注册消息处理器事件
        _messageHandler.MessageReceived += OnMessageReceivedAsync;

        _isRunning = true;

        // TODO: 启动 WebSocket 长连接（需要 FeishuNetSdk.WebSocket 集成）
        // 这里先留空，后续任务完成

        _logger.LogInformation("Feishu channel service started");

        // 等待取消
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Feishu channel service...");
        _isRunning = false;
        _messageHandler.MessageReceived -= OnMessageReceivedAsync;
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 处理收到的消息
    /// </summary>
    private async Task OnMessageReceivedAsync(FeishuIncomingMessage message)
    {
        try
        {
            _logger.LogDebug("Processing message: {MessageId}", message.MessageId);

            // 创建流式回复
            var handle = await SendStreamingMessageAsync(
                message.ChatId,
                _options.ThinkingMessage,
                message.MessageId);

            // TODO: 触发 CLI 执行，这里需要与 CliExecutorService 集成
            // 暂时用模拟响应
            await SimulateAiResponseAsync(handle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message: {MessageId}", message.MessageId);
        }
    }

    /// <summary>
    /// 模拟 AI 响应（后续替换为 CliExecutorService 集成）
    /// </summary>
    private async Task SimulateAiResponseAsync(FeishuStreamingHandle handle)
    {
        var responses = new[] { "正在", "分析", "您的", "问题..." };
        foreach (var text in responses)
        {
            await handle.UpdateAsync($"⏳ {text}");
            await Task.Delay(800);
        }

        await handle.FinishAsync("✅ 这是一个模拟响应。请完成 CliExecutorService 集成以获得真实响应。");
    }

    public async Task<string> SendMessageAsync(string chatId, string content)
    {
        // 使用 CardKit 发送消息
        var cardId = await _cardKit.CreateCardAsync(content, _options.DefaultCardTitle);
        return await _cardKit.SendCardMessageAsync(chatId, cardId);
    }

    public async Task<string> ReplyMessageAsync(string messageId, string content)
    {
        var cardId = await _cardKit.CreateCardAsync(content, _options.DefaultCardTitle);
        return await _cardKit.ReplyCardMessageAsync(messageId, cardId);
    }

    public async Task<FeishuStreamingHandle> SendStreamingMessageAsync(
        string chatId,
        string initialContent,
        string? replyToMessageId = null)
    {
        // 1. 创建流式卡片
        var cardId = await _cardKit.CreateCardAsync(initialContent, _options.DefaultCardTitle);

        // 2. 发送或回复消息
        string messageId;
        if (!string.IsNullOrEmpty(replyToMessageId))
        {
            messageId = await _cardKit.ReplyCardMessageAsync(replyToMessageId, cardId);
        }
        else
        {
            messageId = await _cardKit.SendCardMessageAsync(chatId, cardId);
        }

        _logger.LogDebug("Streaming message created: CardId={CardId}, MessageId={MessageId}", cardId, messageId);

        // 3. 创建流式句柄
        var handle = new FeishuStreamingHandle(
            cardId,
            messageId,
            content => _cardKit.UpdateCardAsync(cardId, content, 0), // sequence 由 handle 内部管理
            content => _cardKit.UpdateCardAsync(cardId, content, 0),
            _options.StreamingThrottleMs);

        return handle;
    }
}
```

**Step 2: 验证编译**

Run: `cd WebCodeCli.Domain && dotnet build --no-restore`
Expected: 编译成功

**Step 3: Commit**

```bash
git add WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs
git commit -m "feat(feishu): 实现飞书渠道主服务（流式回复支持）"
```

---

## Phase 4: 系统集成

### Task 4.1: 添加服务注册扩展

**Files:**
- Modify: `WebCodeCli.Domain/Common/Extensions/ServiceCollectionExtensions.cs`

**Step 1: 添加 AddFeishuChannel 方法**

在 `ServiceCollectionExtensions.cs` 末尾添加：

```csharp
/// <summary>
/// 添加飞书渠道服务
/// </summary>
public static IServiceCollection AddFeishuChannel(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // 绑定配置
    services.Configure<FeishuOptions>(configuration.GetSection("Feishu"));

    // 注册 CardKit 客户端
    services.AddSingleton<IFeishuCardKitClient, FeishuCardKitClient>();

    // 注册消息处理器
    services.AddSingleton<FeishuMessageHandler>();

    // 注册主服务（同时作为 HostedService）
    services.AddSingleton<IFeishuChannelService, FeishuChannelService>();
    services.AddHostedService(sp => sp.GetRequiredService<IFeishuChannelService>());

    return services;
}
```

**Step 2: 验证编译**

Run: `cd WebCodeCli.Domain && dotnet build --no-restore`
Expected: 编译成功

**Step 3: Commit**

```bash
git add WebCodeCli.Domain/Common/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(feishu): 添加服务注册扩展方法"
```

---

### Task 4.2: 更新配置文件

**Files:**
- Modify: `WebCodeCli/appsettings.json`

**Step 1: 添加飞书配置节**

在 `appsettings.json` 根节点添加：

```json
{
  "Feishu": {
    "Enabled": false,
    "AppId": "",
    "AppSecret": "",
    "EncryptKey": "",
    "VerificationToken": "",
    "StreamingThrottleMs": 500,
    "DefaultCardTitle": "小树助手",
    "ThinkingMessage": "⏳ 思考中..."
  }
}
```

**Step 2: Commit**

```bash
git add WebCodeCli/appsettings.json
git commit -m "feat(feishu): 添加飞书渠道配置模板"
```

---

### Task 4.3: 在 Program.cs 中注册服务

**Files:**
- Modify: `WebCodeCli/Program.cs`

**Step 1: 添加服务注册**

在服务注册部分添加：

```csharp
// 添加飞书渠道服务
builder.Services.AddFeishuChannel(builder.Configuration);
```

**Step 2: 验证编译**

Run: `cd WebCodeCli && dotnet build`
Expected: 编译成功

**Step 3: Commit**

```bash
git add WebCodeCli/Program.cs
git commit -m "feat(feishu): 在 Program.cs 中注册飞书渠道服务"
```

---

## Phase 5: 测试与验证

### Task 5.1: 创建单元测试项目

**Files:**
- Create: `WebCodeCli.Tests/WebCodeCli.Tests.csproj`
- Create: `WebCodeCli.Tests/Channels/FeishuCardKitClientTests.cs`

**Step 1: 创建测试项目**

Run: `cd D:/VSWorkshop/WebCode && dotnet new xunit -n WebCodeCli.Tests -o WebCodeCli.Tests`
Expected: 测试项目创建成功

**Step 2: 添加项目引用**

Run: `cd WebCodeCli.Tests && dotnet add reference ../WebCodeCli.Domain/WebCodeCli.Domain.csproj`

**Step 3: 创建 CardKit 客户端测试**

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Service.Channels;
using Xunit;

namespace WebCodeCli.Tests.Channels;

public class FeishuCardKitClientTests
{
    private readonly FeishuOptions _options = new()
    {
        AppId = "cli_test",
        AppSecret = "test_secret"
    };

    [Fact(Skip = "需要真实的飞书凭证")]
    public async Task CreateCardAsync_ShouldReturnCardId()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<FeishuCardKitClient>>();
        var optionsMock = new Mock<IOptions<FeishuOptions>>();
        optionsMock.Setup(x => x.Value).Returns(_options);

        var client = new FeishuCardKitClient(optionsMock.Object, loggerMock.Object);

        // Act
        var cardId = await client.CreateCardAsync("测试内容", "测试标题");

        // Assert
        Assert.NotNull(cardId);
        Assert.NotEmpty(cardId);
    }
}
```

**Step 4: Commit**

```bash
git add WebCodeCli.Tests/
git commit -m "test(feishu): 添加 CardKit 客户端单元测试"
```

---

### Task 5.2: 端到端测试验证

**Step 1: 启动应用**

Run: `cd WebCodeCli && dotnet run`
Expected: 应用启动成功，日志显示 "Feishu channel service started"（如果 Enabled=true）

**Step 2: 手动测试（需要飞书配置）**

1. 在飞书开放平台配置机器人
2. 启用长连接模式
3. 发送消息给机器人
4. 验证是否在 500ms 内收到"思考中"回复
5. 验证卡片是否流式更新

**Step 3: 记录测试结果**

在测试文档中记录测试结果。

---

## 完成检查清单

- [ ] Phase 1: 基础设施完成
  - [ ] FeishuOptions.cs
  - [ ] 消息模型类
  - [ ] NuGet 依赖
- [ ] Phase 2: CardKit 客户端完成
  - [ ] IFeishuCardKitClient
  - [ ] FeishuCardKitClient 实现
- [ ] Phase 3: 核心服务完成
  - [ ] IFeishuChannelService
  - [ ] FeishuMessageHandler
  - [ ] FeishuChannelService 实现
- [ ] Phase 4: 系统集成完成
  - [ ] 服务注册扩展
  - [ ] 配置文件更新
  - [ ] Program.cs 注册
- [ ] Phase 5: 测试验证
  - [ ] 单元测试
  - [ ] 端到端测试

---

## 后续优化（可选）

1. **WebSocket 长连接集成** - 完成 FeishuNetSdk.WebSocket 的完整集成
2. **CliExecutorService 深度集成** - 替换模拟响应为真实 AI 执行
3. **消息持久化** - 添加消息历史记录
4. **错误重试机制** - CardKit API 调用失败时的重试逻辑
5. **性能监控** - 添加响应时间指标
