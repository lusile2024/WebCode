# 飞书渠道快速回复重构设计

> 日期：2026-03-01
> 状态：设计中
> 参考：OpenCowork 飞书实现

## 1. 概述

### 1.1 背景

当前飞书机器人回复响应较慢，用户体验不佳。参考 OpenCowork 项目的实现，采用 **WebSocket 长连接 + 流式卡片** 方案实现快速回复。

### 1.2 目标

- 用户发送消息后，**500ms 内**看到"思考中..."回复
- AI 处理过程中，**流式更新**卡片内容（500ms 节流）
- 处理完成后，显示最终结果

### 1.3 技术选型

| 组件 | 技术 | 说明 |
|------|------|------|
| 长连接 | FeishuNetSdk.WebSocket | 官方 SDK 的 WebSocket 扩展 |
| 事件处理 | FeishuNetSdk.Endpoint | IEventHandler<,> 模式 |
| 消息发送 | FeishuNetSdk (IFeishuTenantApi) | 官方 SDK |
| 流式卡片 | 自封装 CardKitClient | SDK 不支持，需自行封装 |

---

## 2. 架构设计

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           飞书消息处理流程                                │
└─────────────────────────────────────────────────────────────────────────┘

用户发送消息
     │
     ▼
┌─────────────────┐
│ WebSocket 接收  │ ← FeishuNetSdk.WebSocket 长连接
│ (FeishuEvent)   │
└────────┬────────┘
         │
         ▼
┌─────────────────┐     否    ┌─────────────┐
│ 群聊@检测       │─────────→│ 忽略消息    │
└────────┬────────┘           └─────────────┘
         │ 是
         ▼
┌─────────────────┐     否    ┌─────────────┐
│ 消息去重检测    │─────────→│ 忽略重复    │
│ (EventId缓存)   │           └─────────────┘
└────────┬────────┘
         │ 是
         ▼
┌─────────────────────────────────────────────────┐
│ 立即创建流式卡片                                 │
│ 1. CreateCard("⏳ 思考中...", "AI 助手")        │
│ 2. ReplyCardMessage(messageId, cardId)          │
│ 3. 返回 StreamingHandle                         │
└──────────────────────┬──────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────┐
│ 触发 CLI 执行 (CliExecutorService)              │
│ • 绑定 Session                                   │
│ • 流式输出 → StreamingHandle.UpdateAsync()      │
│ • 500ms 节流                                     │
└──────────────────────┬──────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────┐
│ 完成处理                                         │
│ StreamingHandle.FinishAsync(finalContent)       │
└─────────────────────────────────────────────────┘
```

### 2.2 响应时序

| 阶段 | 时间 | 用户看到 |
|------|------|----------|
| 收到消息 | 0ms | - |
| 创建卡片并回复 | <500ms | "⏳ 思考中..." |
| AI 开始输出 | 1-3s | 逐步更新内容 |
| 处理完成 | 视任务复杂度 | 最终结果 |

---

## 3. 配置与模型

### 3.1 配置项

```csharp
// Domain/Common/Options/FeishuOptions.cs
public class FeishuOptions
{
    public bool Enabled { get; set; } = false;
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string EncryptKey { get; set; } = "";
    public string VerificationToken { get; set; } = "";

    // 流式卡片配置
    public int StreamingThrottleMs { get; set; } = 500;
    public string DefaultCardTitle { get; set; } = "AI 助手";
    public string ThinkingMessage { get; set; } = "⏳ 思考中...";
}
```

### 3.2 消息模型

```csharp
// Domain/Model/Channels/FeishuIncomingMessage.cs
public class FeishuIncomingMessage
{
    public string MessageId { get; set; }
    public string ChatId { get; set; }
    public string ChatType { get; set; }  // p2p | group
    public string Content { get; set; }
    public string SenderId { get; set; }
    public string SenderName { get; set; }
    public string? ReplyMessageId { get; set; }
}

// Domain/Model/Channels/FeishuStreamingHandle.cs
public class FeishuStreamingHandle
{
    public string CardId { get; set; }
    public string MessageId { get; set; }

    // 更新卡片内容（带节流）
    public Task UpdateAsync(string content);

    // 完成更新
    public Task FinishAsync(string finalContent);
}
```

---

## 4. 核心服务

### 4.1 服务接口

```csharp
// Domain/Service/Channels/IFeishuChannelService.cs
public interface IFeishuChannelService
{
    bool IsRunning { get; }

    // 启动/停止服务
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);

    // 发送消息
    Task<string> SendMessageAsync(string chatId, string content);
    Task<string> ReplyMessageAsync(string messageId, string content);

    // 流式回复（核心）
    Task<FeishuStreamingHandle> SendStreamingMessageAsync(
        string chatId,
        string initialContent,
        string? replyToMessageId = null);
}
```

### 4.2 CardKit 客户端接口

```csharp
// Domain/Service/Channels/IFeishuCardKitClient.cs
public interface IFeishuCardKitClient
{
    // 创建流式卡片
    Task<string> CreateCardAsync(string initialContent, string title);

    // 更新卡片（sequence 必须递增）
    Task<bool> UpdateCardAsync(string cardId, string content, int sequence);

    // 发送卡片消息
    Task<string> SendCardMessageAsync(string chatId, string cardId);

    // 回复卡片消息
    Task<string> ReplyCardMessageAsync(string replyToMessageId, string cardId);
}
```

### 4.3 CardKit 客户端实现（自封装）

```csharp
// Domain/Service/Channels/FeishuCardKitClient.cs
public class FeishuCardKitClient : IFeishuCardKitClient
{
    private readonly HttpClient _http;
    private readonly FeishuOptions _options;

    private const string BaseUrl = "https://open.feishu.cn/open-apis";
    private string _accessToken;
    private DateTime _tokenExpiresAt;

    public async Task<string> CreateCardAsync(string content, string title)
    {
        await EnsureTokenAsync();

        var cardData = new
        {
            schema = "2.0",
            config = new { update_multi = true, streaming_mode = true },
            header = new { title = new { tag = "plain_text", content = title } },
            body = new
            {
                elements = new[] { new { tag = "markdown", content } }
            }
        };

        var response = await _http.PostAsJsonAsync(
            $"{BaseUrl}/cardkit/v1/cards",
            new { type = "card_json", data = JsonSerializer.Serialize(cardData) });

        // 返回 card_id
    }

    public async Task<bool> UpdateCardAsync(string cardId, string content, int sequence)
    {
        await EnsureTokenAsync();

        var cardData = new
        {
            schema = "2.0",
            config = new { update_multi = true, streaming_mode = true },
            body = new
            {
                elements = new[] { new { tag = "markdown", content } }
            }
        };

        var response = await _http.PutAsJsonAsync(
            $"{BaseUrl}/cardkit/v1/cards/{cardId}",
            new { card = new { type = "card_json", data = JsonSerializer.Serialize(cardData) }, sequence });

        return response.IsSuccessStatusCode;
    }

    // ... SendCardMessageAsync, ReplyCardMessageAsync 实现
}
```

### 4.4 节流更新器

```csharp
// 内部类，用于流式更新节流
public class ThrottledUpdater
{
    private readonly IFeishuCardKitClient _cardKit;
    private readonly int _throttleMs;
    private DateTime _lastUpdate = DateTime.MinValue;
    private int _sequence = 0;
    private readonly string _cardId;

    public async Task UpdateAsync(string content)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastUpdate).TotalMilliseconds < _throttleMs)
            return; // 节流跳过

        _lastUpdate = now;
        _sequence++;
        await _cardKit.UpdateCardAsync(_cardId, content, _sequence);
    }

    public async Task FinishAsync(string finalContent)
    {
        _sequence++;
        await _cardKit.UpdateCardAsync(_cardId, finalContent, _sequence);
    }
}
```

---

## 5. 系统集成

### 5.1 与 CliExecutorService 集成

```
┌───────────────────────────────────────────────────────────────────────┐
│                        现有 CliExecutorService                         │
├───────────────────────────────────────────────────────────────────────┤
│  ExecuteAsync(prompt, session, onOutput)                              │
│       │                                                                │
│       ├── 解析流式事件 (stream-json)                                   │
│       │        │                                                       │
│       │        └── onOutput(event) ──────────────────────┐            │
│       │                                                  │            │
│       └── 返回结果                                       │            │
└───────────────────────────────────────────────────────────────────────┘
                                                          │
                                                          ▼
┌───────────────────────────────────────────────────────────────────────┐
│                     新增 FeishuChannelService                          │
├───────────────────────────────────────────────────────────────────────┤
│  OnOutputHandler(event):                                              │
│       │                                                                │
│       ├── 解析事件内容                                                 │
│       │        │                                                       │
│       │        └── streamingHandle.UpdateAsync(content)               │
│       │               │                                                │
│       │               └── 节流更新飞书卡片                              │
│       │                                                                │
│       └── 完成时: streamingHandle.FinishAsync(finalContent)           │
└───────────────────────────────────────────────────────────────────────┘
```

### 5.2 Session 绑定策略

```csharp
// 飞书消息 → Session 映射
// Key: "feishu:{appId}:chat:{chatId}"
// 示例: "feishu:cli_xxx:chat:oc_xxx"

public class FeishuSessionMapper
{
    public string GetOrCreateSession(string appId, string chatId, string? chatName, string? senderName)
    {
        var externalChatId = $"feishu:{appId}:chat:{chatId}";

        // 查找现有 Session
        var session = _sessionService.FindByExternalChatId(externalChatId);

        if (session == null)
        {
            // 创建新 Session
            session = _sessionService.Create(new Session
            {
                Title = chatName ?? senderName ?? chatId,
                WorkingFolder = Path.Combine(_pluginsWorkDir, appId),
                ExternalChatId = externalChatId,
                PluginId = appId
            });
        }

        return session.Id;
    }
}
```

### 5.3 服务注册

```csharp
// ServiceCollectionExtensions.cs
public static IServiceCollection AddFeishuChannel(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<FeishuOptions>(
        configuration.GetSection("Feishu"));

    // 注册飞书服务
    services.AddSingleton<IFeishuCardKitClient, FeishuCardKitClient>();
    services.AddSingleton<IFeishuChannelService, FeishuChannelService>();

    // 注册为 HostedService
    services.AddHostedService(sp => sp.GetRequiredService<IFeishuChannelService>());

    return services;
}
```

### 5.4 配置文件

```json
// appsettings.json
{
  "Feishu": {
    "Enabled": true,
    "AppId": "cli_xxxxxxxxxxxx",
    "AppSecret": "xxxxxxxxxxxxxxxx",
    "EncryptKey": "xxxxxxxxxxxxxxxx",
    "VerificationToken": "xxxxxxxxxxxxxxxx",
    "StreamingThrottleMs": 500,
    "DefaultCardTitle": "小树助手",
    "ThinkingMessage": "⏳ 思考中..."
  }
}
```

---

## 6. 文件结构

```
WebCodeCli.Domain/
├── Common/Options/
│   └── FeishuOptions.cs                    # 配置项
│
├── Domain/Model/Channels/
│   ├── FeishuIncomingMessage.cs            # 收到的消息模型
│   ├── FeishuStreamingHandle.cs            # 流式句柄
│   └── FeishuSessionMapping.cs             # Session 映射
│
├── Domain/Service/Channels/
│   ├── IFeishuChannelService.cs            # 主服务接口
│   ├── FeishuChannelService.cs             # 主服务实现
│   ├── IFeishuCardKitClient.cs             # CardKit 客户端接口
│   ├── FeishuCardKitClient.cs              # CardKit 客户端（自封装）
│   └── FeishuMessageHandler.cs             # 消息处理器
│
└── Domain/Service/
    └── ServiceCollectionExtensions.cs      # 添加 AddFeishuChannel()
```

---

## 7. 实现计划

| 阶段 | 任务 | 依赖 | 预估时间 |
|------|------|------|----------|
| **Phase 1** | 基础设施 | - | 0.5h |
| 1.1 | 创建 `FeishuOptions.cs` 配置项 | - | |
| 1.2 | 创建消息模型类 | 1.1 | |
| 1.3 | 安装 NuGet 包 | - | |
| **Phase 2** | CardKit 客户端 | Phase 1 | 2h |
| 2.1 | 实现 `FeishuCardKitClient` (CreateCard) | 1.1 | |
| 2.2 | 实现 `UpdateCard` 流式更新 | 2.1 | |
| 2.3 | 单元测试 CardKit 客户端 | 2.2 | |
| **Phase 3** | 核心服务 | Phase 2 | 3h |
| 3.1 | 实现 `FeishuChannelService` 基础结构 | 1.2 | |
| 3.2 | 集成 WebSocket 长连接 | 3.1 | |
| 3.3 | 实现消息路由与 Session 绑定 | 3.2 | |
| 3.4 | 实现流式回复（节流更新） | 3.3, 2.3 | |
| **Phase 4** | 系统集成 | Phase 3 | 1.5h |
| 4.1 | 与 `CliExecutorService` 集成 | 3.4 | |
| 4.2 | 添加配置到 `appsettings.json` | 4.1 | |
| 4.3 | 端到端测试 | 4.2 | |

**总预估时间：7h**

---

## 8. 关键依赖

```xml
<!-- WebCodeCli.Domain.csproj -->
<ItemGroup>
  <PackageReference Include="FeishuNetSdk" Version="latest" />
  <PackageReference Include="FeishuNetSdk.WebSocket" Version="latest" />
  <PackageReference Include="FeishuNetSdk.Endpoint" Version="latest" />
</ItemGroup>
```

---

## 9. 参考

- OpenCowork 飞书实现: `src/main/plugins/providers/feishu/`
- FeishuNetSdk: https://github.com/vicenteyu/FeishuNetSdk
- 飞书 CardKit API: https://open.feishu.cn/document/server-docs/cardkit-v1/card/create
