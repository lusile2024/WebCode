# WebCode 飞书渠道集成设计文档

**版本**: 1.0
**日期**: 2026-02-28
**状态**: 已批准

## 1. 概述

### 1.1 目标

将 WebCode 扩展为支持飞书（Lark）渠道，使用户能够通过飞书聊天界面操作 CLI 工具（Claude Code CLI、Codex CLI、OpenCode CLI 等）。

### 1.2 需求摘要

| 需求项 | 描述 |
|-------|------|
| 使用场景 | 个人使用 |
| 交互方式 | 富媒体交互（卡片、按钮、文件预览） |
| 会话管理 | 多会话切换 |
| 架构方案 | 共享后端，复用现有 CLI 适配器 |
| 文件支持 | 上传/下载 + Git 集成 |
| 异步通知 | 推送通知 + 实时进度卡片 |
| 网络要求 | 无需公网 IP，支持个人飞书用户 |

### 1.3 技术选型

- **连接方式**: 飞书 WebSocket 长连接（`eventMode: "ws"`）
- **优势**: 无需公网 IP、无需内网穿透、个人用户可用

---

## 2. 架构设计

### 2.1 整体架构

采用**单体扩展**方案，在现有 WebCode 项目中新增 FeishuChannel 模块。

```
┌─────────────────────────────────────────────────────────────────┐
│                        WebCode 服务                              │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                   表示层 (Presentation)                  │   │
│  │  ┌─────────────┐  ┌─────────────────────────────────┐   │   │
│  │  │ Blazor UI   │  │ FeishuChannel (新增)            │   │   │
│  │  │ (现有)      │  │ - WebSocket Client              │   │   │
│  │  │             │  │ - Message Handler               │   │   │
│  │  │             │  │ - Card Builder                  │   │   │
│  │  └─────────────┘  └─────────────────────────────────┘   │   │
│  └─────────────────────────────────────────────────────────┘   │
│                              │                                  │
│                              ▼                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                   领域层 (Domain)                        │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐      │   │
│  │  │ ICliTool    │  │ Workspace   │  │ Session     │      │   │
│  │  │ Adapter     │  │ Manager     │  │ Manager     │      │   │
│  │  │ (现有)      │  │ (现有)      │  │ (复用/扩展) │      │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘      │   │
│  └─────────────────────────────────────────────────────────┘   │
│                              │                                  │
│                              ▼                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                   基础设施层 (Infrastructure)            │   │
│  │  Process Executor | File Storage | Git Service          │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
        ▲                                    ▲
        │ HTTP                               │ WebSocket
   浏览器访问                            飞书云平台
```

### 2.2 新增模块结构

```
WebCodeCli.Domain/
├── Domain/
│   ├── Service/
│   │   ├── Adapters/              # 现有 CLI 适配器
│   │   └── Channels/              # 新增：渠道服务
│   │       ├── IChannelService.cs           # 渠道接口
│   │       ├── ChannelType.cs               # 渠道类型枚举
│   │       └── Feishu/
│   │           ├── FeishuChannelService.cs  # 渠道服务主类
│   │           ├── FeishuWebSocketClient.cs # WebSocket 客户端
│   │           ├── FeishuMessageHandler.cs  # 消息处理器
│   │           ├── FeishuCardBuilder.cs     # 卡片构建器
│   │           └── Models/
│   │               ├── FeishuEvent.cs       # 事件模型
│   │               ├── FeishuMessage.cs     # 消息模型
│   │               └── FeishuCard.cs        # 卡片模型
│   └── Model/
│       └── ChannelSession.cs      # 新增：渠道会话关联

WebCodeCli/
├── Controllers/
│   └── FeishuController.cs        # 新增：飞书回调处理（可选）
└── BackgroundServices/
    └── FeishuBackgroundService.cs # 新增：WebSocket 后台服务
```

---

## 3. 核心组件设计

### 3.1 渠道服务接口

```csharp
// IChannelService.cs - 统一渠道接口，为未来扩展预留
public interface IChannelService
{
    string ChannelType { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task SendMessageAsync(string sessionId, string content, CancellationToken cancellationToken = default);
    Task SendCardAsync(string sessionId, object card, CancellationToken cancellationToken = default);
}
```

### 3.2 飞书 WebSocket 客户端

```csharp
// FeishuWebSocketClient.cs - 核心连接管理
public class FeishuWebSocketClient : IDisposable
{
    // 职责：
    // - 建立/维护 WebSocket 长连接
    // - 心跳保活（飞书要求每 30s 发送心跳）
    // - 自动重连机制（指数退避）
    // - 消息收发

    public event EventHandler<FeishuEvent>? OnEventReceived;

    public Task ConnectAsync(string appId, string appSecret);
    public Task SendAsync(object message);
    public Task DisconnectAsync();
}
```

### 3.3 消息处理器

```csharp
// FeishuMessageHandler.cs - 消息路由与转换
public class FeishuMessageHandler
{
    // 职责：
    // - 解析飞书事件（消息、按钮点击、文件上传等）
    // - 路由到对应处理器
    // - 将 CLI 输出转换为飞书消息格式

    private readonly Dictionary<string, Func<FeishuMessage, Task>> _commandHandlers;

    public Task HandleEventAsync(FeishuEvent @event);

    // 支持的交互类型
    Task HandleTextMessageAsync(FeishuMessage message);      // 文本消息
    Task HandleCardActionAsync(FeishuCardAction action);     // 卡片按钮点击
    Task HandleFileUploadAsync(FeishuFile file);             // 文件上传
}
```

### 3.4 卡片构建器

```csharp
// FeishuCardBuilder.cs - 富媒体卡片生成
public class FeishuCardBuilder
{
    public object BuildSessionCard(SessionInfo session);           // 会话信息卡
    public object BuildProgressCard(string taskId, int progress, string status);  // 进度卡
    public object BuildCodePreviewCard(string code, string language);  // 代码预览卡
    public object BuildFileCard(FileInfo file, string downloadUrl);    // 文件卡
    public object BuildConfirmCard(string title, string message, string confirmAction); // 确认对话框
    public object BuildSessionListCard(List<SessionInfo> sessions);    // 会话列表卡
}
```

### 3.5 会话管理扩展

```csharp
// ChannelSession.cs - 渠道会话关联模型
public class ChannelSession
{
    public string Id { get; set; }
    public string ChannelType { get; set; }  // "feishu" | "blazor"
    public string ChannelUserId { get; set; }  // 飞书 open_id
    public string WorkspaceId { get; set; }    // 关联的工作区
    public string CurrentSessionId { get; set; }  // 当前 CLI 会话
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
}

// SessionManager 扩展方法
public class SessionManager  // 现有类扩展
{
    // 新增方法
    public Task<ChannelSession> GetOrCreateByChannelUserAsync(string channelType, string userId);
    public Task BindWorkspaceAsync(string channelId, string workspaceId);
    public Task SwitchSessionAsync(string channelId, string newSessionId);
}
```

### 3.6 后台服务

```csharp
// FeishuBackgroundService.cs - 托管后台服务
public class FeishuBackgroundService : BackgroundService
{
    // 职责：
    // - 应用启动时自动连接飞书 WebSocket
    // - 管理连接生命周期
    // - 处理重连逻辑

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _feishuService.StartAsync(stoppingToken);
        // 保持运行直到应用停止
    }
}
```

### 3.7 组件依赖关系

```
FeishuBackgroundService
        │
        ▼
FeishuChannelService (IChannelService)
        │
        ├──► FeishuWebSocketClient (连接管理)
        │
        ├──► FeishuMessageHandler (消息处理)
        │           │
        │           ├──► SessionManager (会话管理)
        │           ├──► WorkspaceManager (工作区管理)
        │           └──► ICliToolAdapter (CLI 适配器)
        │
        └──► FeishuCardBuilder (卡片生成)
```

---

## 4. 数据流设计

### 4.1 文本消息处理流程（主流程）

```
用户发送: "帮我写个冒泡排序"
        │
        ▼
┌───────────────────────────────────────────────────────────┐
│ 飞书云平台 → WebSocket 推送事件                            │
└───────────────────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────────────────┐
│ FeishuWebSocketClient 接收事件                             │
│ FeishuMessageHandler 解析消息                             │
│ 查找/创建会话                                             │
└───────────────────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────────────────┐
│ 启动 CLI 进程                                             │
│ 监听 CLI 输出流 (SSE/标准输出)                            │
└───────────────────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────────────────┐
│ 实时解析输出 → 构建进度卡片 → 推送至飞书                   │
│ 循环直到完成                                              │
└───────────────────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────────────────┐
│ 发送最终结果卡片                                          │
└───────────────────────────────────────────────────────────┘
```

### 4.2 多会话切换流程

```
用户操作: 点击"切换会话"按钮
        │
        ▼
┌───────────────────────────────────────────────────────────┐
│ 1. 解析卡片动作: action = "switch_session", target = "id" │
│ 2. SessionManager.SwitchSessionAsync(channelId, targetId) │
│ 3. 构建并发送确认卡片                                      │
└───────────────────────────────────────────────────────────┘
```

### 4.3 文件上传/下载流程

**上传流程：**
1. 用户发送文件 → 飞书云平台 → WebSocket 事件
2. `HandleFileUploadAsync()` 获取文件下载 URL
3. 下载文件到工作区目录
4. 通知 CLI 工具
5. 发送确认卡片

**下载流程：**
1. 用户输入 `/download src/main.rs`
2. 验证文件存在于工作区
3. 上传文件到飞书临时存储
4. 构建文件卡片（下载/预览按钮）

### 4.4 异步通知推送策略

| 事件类型 | 推送策略 |
|---------|---------|
| 首条输出 | 立即发送"开始处理"卡片 |
| 进度更新 | 节流发送（每 2 秒最多 1 次） |
| 重要事件 | 立即发送（工具调用、错误、完成） |
| 流结束 | 发送最终结果卡片 |

### 4.5 命令路由表

| 用户输入 | 处理器 | 说明 |
|---------|--------|------|
| 普通文本 | `HandleTextMessageAsync` | 作为 prompt 发送给 CLI |
| `/new` | `HandleNewSessionAsync` | 创建新会话 |
| `/switch [id]` | `HandleSwitchSessionAsync` | 切换会话 |
| `/list` | `HandleListSessionsAsync` | 列出所有会话 |
| `/download [path]` | `HandleDownloadAsync` | 下载文件 |
| `/upload` | `HandleUploadAsync` | 触发文件上传 |
| `/git [cmd]` | `HandleGitCommandAsync` | Git 操作 |
| 卡片按钮点击 | `HandleCardActionAsync` | 根据 action 路由 |

---

## 5. 错误处理

### 5.1 连接错误处理

| 错误类型 | 处理策略 | 用户感知 |
|---------|---------|---------|
| 连接断开 | 自动重连，指数退避 (2s, 4s, 8s, 16s, 32s) | 无感知（30秒内恢复） |
| 认证失败 | 记录错误，停止重试，通知管理员 | 发送系统告警卡片 |
| 心跳超时 | 主动断开，触发重连 | 无感知 |
| 网络波动 | 心跳检测 + 自动重连 | 无感知 |

### 5.2 CLI 执行错误处理

| 错误类型 | 处理策略 |
|---------|---------|
| 可恢复错误 | 继续执行，透传 CLI 消息 |
| 不可恢复错误 | 发送错误卡片，提供修复建议和操作按钮 |
| 超时错误 | 发送超时卡片，提供重试/取消选项 |

### 5.3 资源限制

```csharp
public class ChannelLimits
{
    public int MaxConcurrentSessionsPerUser { get; set; } = 5;
    public int MaxMessageLength { get; set; } = 4000;      // 飞书限制
    public int MaxFileSize { get; set; } = 20 * 1024 * 1024; // 20MB
    public int SessionTimeoutMinutes { get; set; } = 30;
    public int MaxOutputLengthPerMessage { get; set; } = 8000;
}
```

### 5.4 优雅降级策略

| 组件故障 | 降级行为 |
|---------|---------|
| WebSocket 断开 | 暂存消息，重连后批量发送摘要 |
| CLI 工具不可用 | 返回"服务暂时不可用"，记录队列待处理 |
| 文件服务故障 | 禁用文件功能，保留文本交互 |
| 飞书 API 限流 | 消息队列缓冲，限速发送 |

---

## 6. 配置与部署

### 6.1 配置结构

```json
{
  "Channels": {
    "Feishu": {
      "Enabled": true,
      "AppId": "cli_xxxxxxxxxx",
      "AppSecret": "xxxxxxxxxxxxxxxxxxxxxxxx",
      "EventMode": "ws",
      "WebSocket": {
        "HeartbeatIntervalSeconds": 30,
        "ReconnectDelaySeconds": 5,
        "MaxReconnectAttempts": 5
      },
      "Message": {
        "MaxLength": 4000,
        "ThrottleIntervalMs": 2000
      }
    }
  },
  "ChannelLimits": {
    "MaxConcurrentSessionsPerUser": 5,
    "MaxFileSizeBytes": 20971520,
    "SessionTimeoutMinutes": 30
  }
}
```

### 6.2 飞书开放平台配置

1. **创建企业自建应用**：获取 App ID 和 App Secret
2. **启用机器人能力**：配置机器人名称和头像
3. **配置权限**：
   - `im:message` - 获取与发送消息
   - `im:message:send_as_bot` - 以应用身份发消息
   - `im:resource` - 获取与上传文件
   - `contact:user.base` - 获取用户基本信息
4. **配置事件订阅**：
   - 事件模式：长连接 (WebSocket)
   - 订阅事件：`im.message.receive_v1`
5. **发布版本**：提交审核

### 6.3 Docker Compose 部署

```yaml
version: '3.8'
services:
  webcode:
    build: .
    ports:
      - "5000:5000"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Channels__Feishu__AppId=${FEISHU_APP_ID}
      - Channels__Feishu__AppSecret=${FEISHU_APP_SECRET}
    volumes:
      - ./workspaces:/app/workspaces
      - ./data:/app/data
    restart: unless-stopped
```

---

## 7. 实现计划

### Phase 1 - MVP（核心功能）

- [ ] FeishuWebSocketClient - WebSocket 连接管理
- [ ] FeishuMessageHandler - 文本消息处理
- [ ] 基础会话管理
- [ ] 简单文本响应

### Phase 2 - 富媒体交互

- [ ] FeishuCardBuilder - 卡片构建
- [ ] 进度卡片
- [ ] 会话切换卡片
- [ ] 多会话管理

### Phase 3 - 文件与 Git

- [ ] 文件上传处理
- [ ] 文件下载/预览
- [ ] Git 命令集成

### Phase 4 - 完善体验

- [ ] 异步通知优化
- [ ] 错误处理完善
- [ ] 性能优化

---

## 8. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|-----|------|---------|
| 飞书 API 变更 | 中 | 抽象层隔离，便于适配 |
| WebSocket 稳定性 | 高 | 心跳检测 + 自动重连 |
| 消息丢失 | 中 | 消息确认机制 + 本地缓存 |
| 并发冲突 | 低 | 会话锁机制 |

---

## 附录：参考资源

- [飞书开放平台文档](https://open.feishu.cn/document/)
- [飞书 WebSocket 长连接](https://open.feishu.cn/document/client-docs/bot-v3/events/overview)
- [飞书卡片消息](https://open.feishu.cn/document/client-docs/bot-v3/card)
- OpenClaw 架构参考
