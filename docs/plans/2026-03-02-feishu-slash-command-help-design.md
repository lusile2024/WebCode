# 飞书斜杠命令帮助功能设计

> 日期：2026-03-02
> 状态：设计完成
> 参考：Claude Code CLI、飞书开放平台

## 1. 概述

### 1.1 背景

用户希望在飞书机器人中实现类似 Claude Code CLI 的命令提示功能。通过输入 `/feishuhelp` 显示可用命令，点击命令按钮可将命令预填入输入框供用户编辑后执行。

### 1.2 目标

- 用户输入 `/feishuhelp` → 显示命令选择卡片
- 用户输入 `/feishuhelp <keyword>` → 显示过滤后的命令卡片
- 双卡片交互模式：命令选择 → 编辑执行
- 支持三类命令：核心 CLI、项目技能、全局技能、插件
- 支持命令列表刷新功能

### 1.3 技术选型

| 组件 | 技术 | 说明 |
|------|------|------|
| 卡片交互 | 飞书卡片回调 | card.action.trigger 事件 |
| 命令数据 | 静态缓存 + 插件扫描 | 启动加载，支持刷新 |
| 卡片构建 | 原生 JSON 构建 | 不依赖模板 |
| 命令执行 | 复用 CliExecutorService | 现有流式输出 |

---

## 2. 整体架构

### 2.1 核心交互流程

```
用户输入 "/feishuhelp"
    ↓
发送"命令选择卡片"（卡片1）
    ↓
用户点击某个命令按钮
    ↓
回调触发 → 更新为"执行卡片"（卡片2），命令预填入输入框
    ↓
用户编辑输入框内容
    ↓
用户点击"执行命令"或"取消"
    ↓
回调触发 → 执行命令 OR 返回卡片1
```

### 2.2 组件结构

```
WebCodeCli.Domain/
├── Domain/Model/Channels/
│   ├── FeishuCommand.cs              # 命令模型（新增）
│   ├── FeishuCommandCategory.cs      # 命令分组模型（新增）
│   └── FeishuHelpCardAction.cs       # 卡片回调动作模型（新增）
│
├── Domain/Service/Channels/
│   ├── FeishuCommandService.cs       # 命令服务（新增）
│   ├── FeishuHelpCardBuilder.cs      # 帮助卡片构建器（新增）
│   ├── FeishuPluginPathHelper.cs     # 插件路径助手（新增）
│   ├── FeishuMessageHandler.cs       # 现有，添加回调处理
│   └── FeishuChannelService.cs       # 现有，添加命令发送
│
└── Common/Options/
    └── FeishuOptions.cs               # 现有，可能添加配置
```

### 2.3 关键数据模型

**命令模型：**
```csharp
public class FeishuCommand
{
    public string Id { get; set; }           // 唯一标识，如 "help"
    public string Name { get; set; }         // 显示名称，如 "--help"
    public string Description { get; set; }  // 简短描述
    public string Usage { get; set; }        // 用法示例
    public string Category { get; set; }     // 所属分组
}

public class FeishuPluginCommand : FeishuCommand
{
    public string SkillPath { get; set; }    // 插件路径
    public bool IsOfficial { get; set; }     // 是否官方插件
}
```

**命令分组模型：**
```csharp
public class FeishuCommandCategory
{
    public string Id { get; set; }
    public string Name { get; set; }         // 显示名称
    public List<FeishuCommand> Commands { get; set; }
}
```

**回调动作模型：**
```csharp
public class FeishuHelpCardAction
{
    public string Action { get; set; }       // refresh_commands, select_command, etc.
    public string? CommandId { get; set; }   // 命令 ID（select_command 时）
}
```

---

## 3. 卡片设计

### 3.1 卡片 1：命令选择卡片

**布局：**
```
┌─────────────────────────────────────┐
│  🤖 Claude Code CLI 命令帮助        │ ← header
├─────────────────────────────────────┤
│  [🔄 更新命令列表]                  │ ← 特殊按钮（始终在最上方）
│                                     │
│  [📋 调试和输出 ▼]                 │ ← overflow 折叠按钮
│  [🔐 权限和安全 ▼]                 │
│  [💬 会话管理 ▼]                   │
│  [🧠 模型选择 ▼]                    │
│  [⚙️ 其他选项 ▼]                   │
│  [📦 管理命令 ▼]                    │
│  [📁 项目技能 ▼]                    │
│  [🌐 全局技能 ▼]                    │
│  [🔌 插件 ▼]                         │
└─────────────────────────────────────┘
```

**JSON 结构要点：**
- 使用 `overflow` 组件实现折叠按钮组
- 每个分组对应一个 overflow
- 按钮 `value` 包含命令 ID：`{ "action": "select_command", "command_id": "help" }`
- 更新按钮 `value`：`{ "action": "refresh_commands" }`

### 3.2 卡片 2：执行卡片

**布局：**
```
┌─────────────────────────────────────┐
│  ⚙️ 执行命令：--help                │ ← header
├─────────────────────────────────────┤
│  📝 命令说明：                       │
│  显示命令帮助信息                    │ ← div（lark_md）
│                                     │
│  💡 用法示例：                       │
│  `claude --help`                    │
│                                     │
│  ┌───────────────────────────────┐ │
│  │ claude --help                 │ │ ← input 组件
│  └───────────────────────────────┘ │
│                                     │
│  [❌ 取消]  [✅ 执行命令]         │ ← 按钮组
└─────────────────────────────────────┘
```

**JSON 结构要点：**
- 使用 `form` 容器包裹 input 和按钮
- input 组件预设命令内容
- 按钮 `value`：
  - 取消：`{ "action": "back_to_list" }`
  - 执行：`{ "action": "execute_command" }`
- 使用 `form_submit` 类型提交

### 3.3 命令分组规划

| 分组 ID | 分组名称 | 包含内容 |
|---------|---------|---------|
| `core_debug` | 📋 调试和输出 | `-d, --debug, --verbose, -p, --print, --output-format` |
| `core_permission` | 🔐 权限和安全 | `--dangerously-skip-permissions, --permission-mode` |
| `core_session` | 💬 会话管理 | `-c, --continue, -r, --resume, --fork-session, --session-id` |
| `core_model` | 🧠 模型选择 | `--model, --fallback-model` |
| `core_other` | ⚙️ 其他选项 | `--system-prompt, --add-dir, --ide, --version` |
| `core_commands` | 📦 管理命令 | `mcp, plugin, setup-token, doctor, update, install` |
| `skills_project` | 📁 项目技能 | 项目 `skills/` 目录 |
| `skills_global` | 🌐 全局技能 | `~/.claude/skills/` 目录 |
| `plugins` | 🔌 插件 | `~/.claude/plugins/` 目录 |

---

## 4. 交互流程与回调处理

### 4.1 回调事件类型定义

所有按钮点击通过 `value` 字段的 `action` 区分：

| action 值 | 触发场景 | 处理逻辑 |
|-----------|---------|---------|
| `refresh_commands` | 点击"更新命令列表" | 重新加载命令 → 返回更新后的卡片1 |
| `select_command` | 点击某个命令 | 查找命令详情 → 返回卡片2（预填命令） |
| `back_to_list` | 点击"取消" | 直接返回卡片1 |
| `execute_command` | 点击"执行命令" | 获取输入框内容 → 发送给 CLI 执行 |

### 4.2 详细交互时序

**场景 1：用户输入 `/feishuhelp`**
```
1. FeishuMessageHandler 检测到消息内容为 "/feishuhelp"
2. 调用 FeishuHelpCardBuilder.BuildCommandListCard()
3. 通过 FeishuChannelService 发送卡片
```

**场景 2：用户输入 `/feishuhelp debug`**
```
1. FeishuMessageHandler 检测到 "/feishuhelp <keyword>"
2. 调用 FeishuCommandService.FilterCommands("debug")
3. 构建过滤后的卡片（可能只显示匹配的分组/命令）
4. 发送卡片
```

**场景 3：点击"更新命令列表"**
```
1. 收到 card.action.trigger 回调，action=refresh_commands
2. FeishuCommandService.RefreshCommandsAsync()
3. 重新加载命令列表
4. 返回 toast: "✅ 命令列表已更新" + 新的卡片1
```

**场景 4：选择某个命令**
```
1. 收到 card.action.trigger 回调，action=select_command, command_id="help"
2. FeishuCommandService.GetCommand("help")
3. FeishuHelpCardBuilder.BuildExecuteCard(command)
4. 返回卡片2（输入框预设 "claude --help"）
```

**场景 5：取消执行**
```
1. 收到 card.action.trigger 回调，action=back_to_list
2. 直接返回卡片1
```

**场景 6：执行命令**
```
1. 收到 card.action.trigger 回调，action=execute_command
2. 从 form_value 获取输入框内容
3. 调用现有 CliExecutorService 执行命令
4. 返回 toast: "🚀 开始执行命令..."
5. 同时通过流式卡片展示执行结果（复用现有逻辑）
```

### 4.3 FeishuCommandService 设计

```csharp
public class FeishuCommandService
{
    // 静态缓存的命令列表
    private static List<FeishuCommand>? _cachedCommands;
    private static readonly object _lock = new();

    // 获取命令列表（带缓存）
    public Task<List<FeishuCommand>> GetCommandsAsync();

    // 刷新命令列表
    public Task RefreshCommandsAsync();

    // 根据 ID 获取单个命令
    public Task<FeishuCommand?> GetCommandAsync(string commandId);

    // 过滤命令（支持关键字搜索）
    public Task<List<FeishuCommandCategory>> FilterCommandsAsync(string keyword);

    // 按分组组织命令
    public Task<List<FeishuCommandCategory>> GetCategorizedCommandsAsync();

    // 加载插件命令
    private Task<List<FeishuCommand>> LoadPluginCommandsAsync();

    // 扫描技能目录
    private Task<List<FeishuCommand>> ScanSkillsDirectoryAsync(string dir, string category);

    // 扫描插件目录
    private Task<List<FeishuCommand>> ScanPluginsDirectoryAsync(string dir);
}
```

### 4.4 FeishuHelpCardBuilder 设计

```csharp
public class FeishuHelpCardBuilder
{
    // 构建命令选择卡片（卡片1）
    public string BuildCommandListCard(List<FeishuCommandCategory> categories);

    // 构建执行卡片（卡片2）
    public string BuildExecuteCard(FeishuCommand command);

    // 构建过滤后的卡片
    public string BuildFilteredCard(List<FeishuCommandCategory> categories, string keyword);

    // 构建更新成功的 toast
    private object BuildToast(string message, string type = "info");
}
```

---

## 5. 数据流程与错误处理

### 5.1 命令数据加载流程

**初始加载：**
```
应用启动
    ↓
FeishuCommandService 静态构造函数
    ↓
调用 LoadDefaultCommands()
    ↓
加载内置的 Claude Code CLI 命令定义
    ↓
调用 LoadPluginCommandsAsync()
    ↓
扫描项目技能、全局技能、插件
    ↓
合并所有命令
    ↓
存入 _cachedCommands
```

**刷新流程：**
```
用户点击"更新命令列表"
    ↓
RefreshCommandsAsync()
    ↓
清空 _cachedCommands
    ↓
重新执行完整加载流程
    ↓
返回更新后的卡片
```

### 5.2 插件路径配置

**各平台路径：**

| 平台 | 插件目录 | 技能目录 |
|------|---------|---------|
| Windows | `%USERPROFILE%\.claude\plugins` | `%USERPROFILE%\.claude\skills` |
| macOS / Linux | `~/.claude/plugins` | `~/.claude/skills` |

**代码中路径获取：**
```csharp
public static class FeishuPluginPathHelper
{
    public static string GetPluginsDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".claude", "plugins");
    }

    public static string GetSkillsDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".claude", "skills");
    }

    public static string GetProjectSkillsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "skills");
    }
}
```

### 5.3 错误处理策略

| 场景 | 处理方式 | 用户反馈 |
|------|---------|---------|
| 命令列表未加载 | 返回空状态卡片 | toast: "⚠️ 命令列表加载中，请稍候" |
| 命令 ID 不存在 | 返回卡片1 | toast: "❌ 命令不存在，已返回命令列表" |
| 回调超时（3秒） | 飞书客户端显示错误 | 无（依赖飞书平台）|
| 执行命令失败 | 显示错误信息 | 通过流式卡片展示错误 |
| 关键词无匹配 | 显示空状态卡片 | "未找到匹配的命令，请尝试其他关键词" |
| 插件目录不存在 | 跳过该类型 | 不显示对应分组 |

### 5.4 与现有系统集成

**与 FeishuMessageHandler 集成：**
```csharp
// 在 HandleMessageReceiveAsync 中添加
if (content.StartsWith("/feishuhelp"))
{
    var keyword = content.Substring("/feishuhelp".Length).Trim();
    await SendHelpCardAsync(chatId, messageId, keyword);
    return;
}

// 添加回调处理方法
private async Task<object?> HandleCardActionAsync(CardAction action)
{
    var actionValue = ParseActionValue(action.Value);
    return actionValue.Action switch
    {
        "refresh_commands" => await HandleRefreshCommandsAsync(),
        "select_command" => await HandleSelectCommandAsync(actionValue.CommandId),
        "back_to_list" => await HandleBackToListAsync(),
        "execute_command" => await HandleExecuteCommandAsync(action.FormValue),
        _ => null
    };
}
```

**与 CliExecutorService 集成：**
- 复用现有的流式输出逻辑
- 执行命令时，创建 FeishuStreamingHandle
- 将命令输出流式更新到卡片

---

## 6. 文件结构与实现计划

### 6.1 新增/修改文件清单

```
WebCodeCli.Domain/
├── Domain/Model/Channels/
│   ├── FeishuCommand.cs                    # 新增：命令模型
│   ├── FeishuCommandCategory.cs            # 新增：命令分组模型
│   └── FeishuHelpCardAction.cs            # 新增：卡片回调动作模型
│
├── Domain/Service/Channels/
│   ├── FeishuCommandService.cs             # 新增：命令服务
│   ├── FeishuHelpCardBuilder.cs            # 新增：帮助卡片构建器
│   ├── FeishuPluginPathHelper.cs           # 新增：插件路径助手
│   ├── FeishuMessageHandler.cs             # 修改：添加回调处理
│   └── FeishuChannelService.cs             # 修改：添加卡片发送
│
└── Resources/
    └── FeishuCommands.json                 # 新增：内置命令定义（可选）
```

### 6.2 实现阶段划分

| 阶段 | 任务 | 预估时间 |
|------|------|---------|
| **Phase 1** | 模型与基础结构 | 0.5h |
| 1.1 | 创建 FeishuCommand、FeishuCommandCategory 模型 | |
| 1.2 | 创建 FeishuPluginPathHelper | |
| 1.3 | 创建 FeishuHelpCardAction 回调模型 | |
| **Phase 2** | 命令服务 | 1h |
| 2.1 | 实现 FeishuCommandService 基础结构 | |
| 2.2 | 加载内置核心命令（从 claude-help.md） | |
| 2.3 | 实现插件/技能扫描逻辑 | |
| **Phase 3** | 卡片构建器 | 1.5h |
| 3.1 | 实现 BuildCommandListCard（卡片1） | |
| 3.2 | 实现 BuildExecuteCard（卡片2） | |
| 3.3 | 实现 BuildFilteredCard（过滤） | |
| **Phase 4** | 回调处理集成 | 1h |
| 4.1 | 修改 FeishuMessageHandler，添加 /feishuhelp 检测 | |
| 4.2 | 添加 card.action.trigger 回调处理 | |
| 4.3 | 实现五个 action 的处理逻辑 | |
| **Phase 5** | 测试与完善 | 0.5h |
| 5.1 | 端到端测试 | |
| 5.2 | 错误处理完善 | |

**总预估时间：4.5h**

### 6.3 配置项（可选）

```json
// appsettings.json
"Feishu": {
  // ... 现有配置 ...
  "Help": {
    "Enabled": true,
    "EnablePluginScanning": true,
    "CommandCacheMinutes": 60
  }
}
```

---

## 7. 参考

- Claude Code CLI 帮助文档：`cli/claude-help.md`
- 飞书卡片组件：https://open.feishu.cn/document/feishu-cards/card-components/interactive-components/overflow
- 飞书卡片回调：https://open.feishu.cn/document/feishu-cards/card-callback-communication
- 现有飞书集成：`docs/plans/2026-02-28-feishu-channel-design.md`

