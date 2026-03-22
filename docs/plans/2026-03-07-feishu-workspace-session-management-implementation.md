# 飞书渠道工作目录与会话管理功能实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 实现飞书渠道多会话管理和工作目录切换功能，与网页版行为保持一致，支持用户通过卡片交互管理多个并行工作会话。

**Architecture:** 复用现有CliExecutorService的多会话隔离能力，扩展FeishuChannelService的数据结构支持1对多聊天-会话映射，通过飞书交互式卡片实现会话管理和目录选择功能，保持与网页版完全一致的工作目录清理策略。

**Tech Stack:** .NET 10.0 C#, 飞书卡片SDK, 依赖注入, 异步编程

---

## 前置条件
- 现有飞书渠道服务正常运行
- CliExecutorService多会话功能已实现
- 飞书卡片交互框架已存在

---

### Task 1: 扩展FeishuChannelService数据结构和基础方法

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs:34-40`
- Add: 新的私有字段和辅助方法

**Step 1: 替换单会话映射为多会话映射**
```csharp
// 飞书聊天会话到应用会话的映射（1对多）
private readonly Dictionary<string, List<string>> _chatSessionList = new();
// 飞书聊天当前活跃会话
private readonly Dictionary<string, string> _chatCurrentSession = new();
// 会话最后活跃时间
private readonly Dictionary<string, DateTime> _sessionLastActiveTime = new();
```

**Step 2: 添加获取/创建会话的新方法**
```csharp
/// <summary>
/// 获取聊天的当前活跃会话
/// </summary>
private string GetOrCreateCurrentSession(FeishuIncomingMessage message)
{
    var chatKey = $"feishu:{_options.AppId}:{message.ChatId}";

    // 如果没有当前会话，创建新的
    if (!_chatCurrentSession.TryGetValue(chatKey, out var currentSessionId) ||
        string.IsNullOrEmpty(currentSessionId))
    {
        currentSessionId = CreateNewSession(message);
        _chatCurrentSession[chatKey] = currentSessionId;
    }

    // 更新活跃时间
    _sessionLastActiveTime[currentSessionId] = DateTime.Now;

    return currentSessionId;
}

/// <summary>
/// 创建新会话
/// </summary>
private string CreateNewSession(FeishuIncomingMessage message, string? customWorkspacePath = null)
{
    var chatKey = $"feishu:{_options.AppId}:{message.ChatId}";
    var newSessionId = Guid.NewGuid().ToString();

    // 初始化工作目录
    if (!string.IsNullOrEmpty(customWorkspacePath))
    {
        // TODO: 实现自定义目录设置（后续任务）
    }
    else
    {
        // 使用默认目录
        _cliExecutor.GetSessionWorkspacePath(newSessionId);
    }

    // 添加到会话列表
    if (!_chatSessionList.TryGetValue(chatKey, out var sessionList))
    {
        sessionList = new List<string>();
        _chatSessionList[chatKey] = sessionList;
    }
    sessionList.Add(newSessionId);
    _sessionLastActiveTime[newSessionId] = DateTime.Now;

    _logger.LogInformation(
        "Created new session {SessionId} for chat {ChatId}",
        newSessionId, message.ChatId);

    return newSessionId;
}

/// <summary>
/// 获取聊天的所有会话
/// </summary>
private List<string> GetChatSessions(string chatKey)
{
    return _chatSessionList.TryGetValue(chatKey, out var sessions)
        ? sessions
        : new List<string>();
}
```

**Step 3: 替换原有的GetOrCreateSession方法调用**
在OnMessageReceivedAsync方法中，将第120行的：
```csharp
var sessionId = GetOrCreateSession(message);
```
替换为：
```csharp
var sessionId = GetOrCreateCurrentSession(message);
```

**Step 4: 编译验证**
Run: `dotnet build WebCodeCli.Domain`
Expected: 编译成功，无错误

---

### Task 2: 实现/sessions命令处理逻辑

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCommandService.cs`
- Add: /sessions命令处理逻辑

**Step 1: 添加会话命令处理方法**
```csharp
/// <summary>
/// 处理/sessions命令
/// </summary>
public async Task HandleSessionsCommandAsync(FeishuIncomingMessage message)
{
    var chatKey = $"feishu:{_options.AppId}:{message.ChatId}";
    var sessions = _feishuChannelService.GetChatSessions(chatKey);
    var currentSessionId = _feishuChannelService.GetCurrentSession(chatKey);

    // 构建会话管理卡片
    var card = BuildSessionManagementCard(sessions, currentSessionId, chatKey);

    // 发送卡片
    await _cardKitClient.ReplyCardMessageAsync(message.MessageId, card);
}

/// <summary>
/// 构建会话管理卡片
/// </summary>
private object BuildSessionManagementCard(List<string> sessions, string currentSessionId, string chatKey)
{
    var sessionItems = new List<object>();

    foreach (var sessionId in sessions.Take(10)) // 最多显示10个最近的会话
    {
        var workspacePath = _cliExecutorService.GetSessionWorkspacePath(sessionId);
        var lastActiveTime = _feishuChannelService.GetSessionLastActiveTime(sessionId);
        var isCurrent = sessionId == currentSessionId;

        sessionItems.Add(new
        {
            tag = "div",
            text = new
            {
                tag = "lark_md",
                content = $"{(isCurrent ? "✅ " : "")}**会话ID: {sessionId[..8]}...**\n📂 {workspacePath}\n⏱️ {lastActiveTime:yyyy-MM-dd HH:mm}"
            },
            actions = new[]
            {
                new
                {
                    tag = "button",
                    text = new { tag = "plain_text", content = isCurrent ? "当前" : "切换" },
                    type = isCurrent ? "default" : "primary",
                    value = new
                    {
                        action = "switch_session",
                        session_id = sessionId,
                        chat_key = chatKey
                    }
                },
                new
                {
                    tag = "button",
                    text = new { tag = "plain_text", content = "关闭" },
                    type = "danger",
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

    // 底部操作按钮
    var footerActions = new[]
    {
        new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "➕ 新建会话" },
            type = "primary",
            value = new
            {
                action = "show_create_session",
                chat_key = chatKey
            }
        },
        new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "🧹 清理空闲会话" },
            type = "default",
            value = new
            {
                action = "clean_idle_sessions",
                chat_key = chatKey
            }
        }
    };

    return new
    {
        header = new { title = new { tag = "plain_text", content = "📋 会话管理" } },
        elements = sessionItems.Concat(new[] { new { tag = "hr" } }).Concat(footerActions).ToArray()
    };
}
```

**Step 2: 注册命令路由**
在命令处理入口添加：
```csharp
if (message.Content.Trim().Equals("/sessions", StringComparison.OrdinalIgnoreCase))
{
    await HandleSessionsCommandAsync(message);
    return;
}
```

**Step 3: 编译验证**
Run: `dotnet build WebCodeCli.Domain`
Expected: 编译成功

---

### Task 3: 实现卡片动作处理逻辑

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuMessageHandler.cs`
- Add: 会话相关动作处理

**Step 1: 添加会话切换处理**
```csharp
private async Task HandleSwitchSessionAction(FeishuCardAction action)
{
    var sessionId = action.Value.GetString("session_id");
    var chatKey = action.Value.GetString("chat_key");

    if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(chatKey))
    {
        await ReplyMessageAsync(action.MessageId, "❌ 参数错误，切换失败");
        return;
    }

    _feishuChannelService.SwitchCurrentSession(chatKey, sessionId);
    var workspacePath = _cliExecutorService.GetSessionWorkspacePath(sessionId);

    await ReplyMessageAsync(action.MessageId,
        $"✅ 已切换到会话 {sessionId[..8]}...\n📂 当前工作目录: {workspacePath}");
}
```

**Step 2: 添加关闭会话处理**
```csharp
private async Task HandleCloseSessionAction(FeishuCardAction action)
{
    var sessionId = action.Value.GetString("session_id");
    var chatKey = action.Value.GetString("chat_key");

    if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(chatKey))
    {
        await ReplyMessageAsync(action.MessageId, "❌ 参数错误，关闭失败");
        return;
    }

    // 清理会话
    _feishuChannelService.CloseSession(chatKey, sessionId);

    await ReplyMessageAsync(action.MessageId,
        $"🗑️ 已关闭会话 {sessionId[..8]}...\n工作目录已清理");
}
```

**Step 3: 添加新建会话处理**
```csharp
private async Task HandleCreateSessionAction(FeishuCardAction action)
{
    var chatKey = action.Value.GetString("chat_key");
    var customPath = action.Value.GetString("workspace_path");

    // 从消息中获取用户信息
    var message = new FeishuIncomingMessage
    {
        ChatId = chatKey.Split(':').Last(),
        SenderName = action.UserName
    };

    var newSessionId = _feishuChannelService.CreateNewSession(message, customPath);
    var workspacePath = _cliExecutorService.GetSessionWorkspacePath(newSessionId);

    await ReplyMessageAsync(action.MessageId,
        $"✅ 已创建新会话 {newSessionId[..8]}...\n📂 工作目录: {workspacePath}\n已自动切换到新会话");
}
```

**Step 4: 注册动作路由**
在动作处理入口添加：
```csharp
var actionType = action.Value.GetString("action");
switch (actionType)
{
    case "switch_session":
        await HandleSwitchSessionAction(action);
        break;
    case "close_session":
        await HandleCloseSessionAction(action);
        break;
    case "show_create_session":
        await ShowCreateSessionForm(action);
        break;
    case "create_session":
        await HandleCreateSessionAction(action);
        break;
    case "clean_idle_sessions":
        await HandleCleanIdleSessionsAction(action);
        break;
}
```

**Step 5: 编译验证**
Run: `dotnet build WebCodeCli.Domain`
Expected: 编译成功

---

### Task 4: 集成到/feishuhelp卡片

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCommandService.cs`
- Update: 帮助卡片内容

**Step 1: 更新帮助卡片的命令列表**
在`/feishuhelp`命令返回的卡片中添加：
```csharp
new
{
    tag = "div",
    text = new
    {
        tag = "lark_md",
        content = "**📋 会话管理**\n`/sessions` - 打开会话管理面板，支持多会话切换和工作目录设置"
    }
},
```

**Step 2: 添加会话管理入口按钮**
在帮助卡片的操作区域添加：
```csharp
new
{
    tag = "button",
    text = new { tag = "plain_text", content = "📋 会话管理" },
    type = "primary",
    value = new { action = "show_session_management" }
}
```

**Step 3: 编译验证**
Run: `dotnet build WebCodeCli.Domain`
Expected: 编译成功

---

### Task 5: 安全性与清理逻辑实现

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
- Add: 会话清理和路径校验方法

**Step 1: 实现会话关闭方法**
```csharp
/// <summary>
/// 关闭指定会话
/// </summary>
public void CloseSession(string chatKey, string sessionId)
{
    // 从会话列表移除
    if (_chatSessionList.TryGetValue(chatKey, out var sessions))
    {
        sessions.Remove(sessionId);
    }

    // 如果关闭的是当前会话，清空当前会话
    if (_chatCurrentSession.TryGetValue(chatKey, out var current) && current == sessionId)
    {
        _chatCurrentSession.Remove(chatKey);
    }

    // 移除活跃时间记录
    _sessionLastActiveTime.Remove(sessionId);

    // 清理工作目录（与网页版一致，仅清理系统创建的会话目录）
    _cliExecutor.CleanupSessionWorkspace(sessionId);

    _logger.LogInformation("Closed session {SessionId} for chat {ChatId}", sessionId, chatKey);
}
```

**Step 2: 实现路径安全校验**
```csharp
/// <summary>
/// 验证工作目录路径是否合法
/// </summary>
private bool ValidateWorkspacePath(string path)
{
    var workspaceRoot = _cliExecutor.GetEffectiveWorkspaceRoot();
    var fullPath = Path.GetFullPath(path);

    // 必须在工作区根目录下
    if (!fullPath.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    // 防止路径穿越
    if (fullPath.Contains("..") || fullPath.Contains("~"))
    {
        return false;
    }

    return Directory.Exists(fullPath);
}
```

**Step 3: 实现空闲会话清理**
```csharp
/// <summary>
/// 清理超过24小时未活跃的会话
/// </summary>
public int CleanIdleSessions(string chatKey)
{
    var cleanedCount = 0;
    var cutoffTime = DateTime.Now.AddHours(-24);

    if (_chatSessionList.TryGetValue(chatKey, out var sessions))
    {
        var idleSessions = sessions.Where(s =>
            _sessionLastActiveTime.TryGetValue(s, out var lastActive) &&
            lastActive < cutoffTime).ToList();

        foreach (var sessionId in idleSessions)
        {
            CloseSession(chatKey, sessionId);
            cleanedCount++;
        }
    }

    return cleanedCount;
}
```

**Step 4: 编译验证**
Run: `dotnet build WebCodeCli.Domain`
Expected: 编译成功

---

### Task 6: 功能测试与验证

**Step 1: 运行应用**
Run: `dotnet run --project WebCodeCli`
Expected: 应用正常启动，飞书渠道无错误

**Step 2: 测试基础功能**
1. 发送普通消息，验证自动创建会话功能正常
2. 发送`/sessions`命令，验证会话管理卡片正常显示
3. 点击新建会话，验证新会话创建成功并自动切换
4. 点击切换会话，验证会话切换功能正常
5. 点击关闭会话，验证会话关闭和目录清理正常
6. 发送`/feishuhelp`，验证帮助卡片包含会话管理入口

**Step 3: 测试兼容性**
1. 验证历史消息和会话不受影响
2. 验证CLI执行功能正常，工作目录切换正确
3. 验证多聊天窗口独立会话，互不干扰

---

### Task 7: 文档与提交

**Step 1: 更新CHANGELOG**
添加功能说明到CHANGELOG.md

**Step 2: 提交代码**
```bash
git add WebCodeCli.Domain/Domain/Service/Channels/ docs/plans/
git commit -m "feat(飞书): 实现多会话管理和工作目录切换功能"
```

**Step 3: 推送代码**
```bash
git push origin main
```
