# 飞书斜杠命令帮助功能实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 为飞书机器人实现 `/feishuhelp` 命令帮助功能，支持双卡片交互模式。

**Architecture:** 使用纯卡片回调更新（方法A+），命令缓存在静态变量中，支持刷新。双卡片模式：命令选择卡片 → 执行卡片。

**Tech Stack:** ASP.NET Core, FeishuNetSdk, 飞书卡片 JSON v2.0

---

## 前置准备

**先阅读相关文件：**
- `docs/plans/2026-03-02-feishu-slash-command-help-design.md` - 完整设计文档
- `cli/claude-help.md` - Claude Code CLI 命令列表
- `WebCodeCli.Domain/Domain/Service/Channels/FeishuMessageHandler.cs` - 现有消息处理器
- `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardKitClient.cs` - 现有卡片客户端

---

## Task 1: 创建数据模型

**Files:**
- Create: `WebCodeCli.Domain/Domain/Model/Channels/FeishuCommand.cs`
- Create: `WebCodeCli.Domain/Domain/Model/Channels/FeishuCommandCategory.cs`
- Create: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`

**Step 1: 创建 FeishuCommand 模型**

```csharp
namespace WebCodeCli.Domain.Domain.Model.Channels;

public class FeishuCommand
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public class FeishuPluginCommand : FeishuCommand
{
    public string SkillPath { get; set; } = string.Empty;
    public bool IsOfficial { get; set; }
}
```

**Step 2: 创建 FeishuCommandCategory 模型**

```csharp
namespace WebCodeCli.Domain.Domain.Model.Channels;

public class FeishuCommandCategory
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<FeishuCommand> Commands { get; set; } = new();
}
```

**Step 3: 创建 FeishuHelpCardAction 模型**

```csharp
using System.Text.Json.Serialization;

namespace WebCodeCli.Domain.Domain.Model.Channels;

public class FeishuHelpCardAction
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("command_id")]
    public string? CommandId { get; set; }
}
```

**Step 4: 验证编译**

Build 项目确保无错误。

---

## Task 2: 创建插件路径助手

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/Channels/FeishuPluginPathHelper.cs`

**Step 1: 实现 FeishuPluginPathHelper**

```csharp
namespace WebCodeCli.Domain.Domain.Service.Channels;

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
        // 项目根目录下的 skills 文件夹
        var baseDir = AppContext.BaseDirectory;
        // 向上查找直到找到 skills 文件夹或到达根目录
        var currentDir = baseDir;
        while (!string.IsNullOrEmpty(currentDir))
        {
            var skillsDir = Path.Combine(currentDir, "skills");
            if (Directory.Exists(skillsDir))
            {
                return skillsDir;
            }
            var parent = Directory.GetParent(currentDir);
            currentDir = parent?.FullName ?? string.Empty;
        }
        return Path.Combine(baseDir, "skills");
    }
}
```

**Step 2: 验证编译**

Build 项目确保无错误。

---

## Task 3: 创建命令服务基础结构

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCommandService.cs`

**Step 1: 创建 FeishuCommandService 基础类**

```csharp
using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;
using Microsoft.Extensions.Logging;

namespace WebCodeCli.Domain.Domain.Service.Channels;

public class FeishuCommandService
{
    private static List<FeishuCommand>? _cachedCommands;
    private static readonly object _lock = new();
    private readonly ILogger<FeishuCommandService> _logger;

    public FeishuCommandService(ILogger<FeishuCommandService> logger)
    {
        _logger = logger;
    }

    public async Task<List<FeishuCommand>> GetCommandsAsync()
    {
        if (_cachedCommands == null)
        {
            await LoadCommandsAsync();
        }
        return _cachedCommands ?? new List<FeishuCommand>();
    }

    public async Task RefreshCommandsAsync()
    {
        _logger.LogInformation("🔄 [FeishuHelp] 刷新命令列表");
        lock (_lock)
        {
            _cachedCommands = null;
        }
        await LoadCommandsAsync();
        _logger.LogInformation("✅ [FeishuHelp] 命令列表已刷新，共 {Count} 个命令", _cachedCommands?.Count ?? 0);
    }

    public async Task<FeishuCommand?> GetCommandAsync(string commandId)
    {
        var commands = await GetCommandsAsync();
        return commands.FirstOrDefault(c => c.Id == commandId);
    }

    public async Task<List<FeishuCommandCategory>> GetCategorizedCommandsAsync()
    {
        var commands = await GetCommandsAsync();
        return commands
            .GroupBy(c => c.Category)
            .Select(g => new FeishuCommandCategory
            {
                Id = g.Key,
                Name = GetCategoryName(g.Key),
                Commands = g.ToList()
            })
            .OrderBy(c => c.Id)
            .ToList();
    }

    public async Task<List<FeishuCommandCategory>> FilterCommandsAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return await GetCategorizedCommandsAsync();
        }

        var commands = await GetCommandsAsync();
        var lowerKeyword = keyword.ToLowerInvariant();
        var filtered = commands.Where(c =>
            c.Name.ToLowerInvariant().Contains(lowerKeyword) ||
            c.Description.ToLowerInvariant().Contains(lowerKeyword) ||
            c.Id.ToLowerInvariant().Contains(lowerKeyword))
            .ToList();

        return filtered
            .GroupBy(c => c.Category)
            .Select(g => new FeishuCommandCategory
            {
                Id = g.Key,
                Name = GetCategoryName(g.Key),
                Commands = g.ToList()
            })
            .OrderBy(c => c.Id)
            .ToList();
    }

    private async Task LoadCommandsAsync()
    {
        lock (_lock)
        {
            if (_cachedCommands != null)
                return;
        }

        var commands = new List<FeishuCommand>();

        // 1. 加载核心命令
        commands.AddRange(LoadCoreCommands());

        // 2. 加载插件命令
        try
        {
            var pluginCommands = await LoadPluginCommandsAsync();
            commands.AddRange(pluginCommands);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ [FeishuHelp] 加载插件命令失败");
        }

        lock (_lock)
        {
            _cachedCommands = commands;
        }
    }

    private List<FeishuCommand> LoadCoreCommands()
    {
        // 从 claude-help.md 解析命令
        // 这里先硬编码，后续可优化
        return new List<FeishuCommand>
        {
            // 调试和输出
            new() { Id = "debug", Name = "-d, --debug", Description = "启用调试模式", Usage = "claude --debug", Category = "core_debug" },
            new() { Id = "verbose", Name = "--verbose", Description = "详细模式", Usage = "claude --verbose", Category = "core_debug" },
            new() { Id = "print", Name = "-p, --print", Description = "打印响应并退出", Usage = "claude --print \"你的提示\"", Category = "core_debug" },
            new() { Id = "output_format", Name = "--output-format", Description = "输出格式：text, json, stream-json", Usage = "claude --print --output-format json", Category = "core_debug" },

            // 权限和安全
            new() { Id = "skip_permissions", Name = "--dangerously-skip-permissions", Description = "绕过所有权限检查", Usage = "claude --dangerously-skip-permissions", Category = "core_permission" },
            new() { Id = "permission_mode", Name = "--permission-mode", Description = "权限模式：acceptEdits, bypassPermissions, default, plan", Usage = "claude --permission-mode plan", Category = "core_permission" },

            // 会话管理
            new() { Id = "continue", Name = "-c, --continue", Description = "继续最近的对话", Usage = "claude --continue", Category = "core_session" },
            new() { Id = "resume", Name = "-r, --resume", Description = "恢复指定对话", Usage = "claude --resume <session-id>", Category = "core_session" },
            new() { Id = "fork_session", Name = "--fork-session", Description = "恢复时创建新会话", Usage = "claude --resume --fork-session", Category = "core_session" },

            // 模型选择
            new() { Id = "model", Name = "--model", Description = "选择模型：sonnet, opus, haiku", Usage = "claude --model sonnet", Category = "core_model" },
            new() { Id = "fallback_model", Name = "--fallback-model", Description = "模型过载时的回退模型", Usage = "claude --fallback-model haiku", Category = "core_model" },

            // 其他选项
            new() { Id = "system_prompt", Name = "--system-prompt", Description = "自定义系统提示", Usage = "claude --system-prompt \"你是一个程序员\"", Category = "core_other" },
            new() { Id = "add_dir", Name = "--add-dir", Description = "添加可访问目录", Usage = "claude --add-dir /path/to/project", Category = "core_other" },
            new() { Id = "version", Name = "-v, --version", Description = "输出版本号", Usage = "claude --version", Category = "core_other" },
            new() { Id = "help", Name = "-h, --help", Description = "显示帮助", Usage = "claude --help", Category = "core_other" },

            // 管理命令
            new() { Id = "cmd_mcp", Name = "mcp", Description = "配置和管理 MCP 服务器", Usage = "claude mcp", Category = "core_commands" },
            new() { Id = "cmd_plugin", Name = "plugin", Description = "管理 Claude Code 插件", Usage = "claude plugin", Category = "core_commands" },
            new() { Id = "cmd_doctor", Name = "doctor", Description = "检查 Claude Code 健康状况", Usage = "claude doctor", Category = "core_commands" },
            new() { Id = "cmd_update", Name = "update", Description = "检查更新并安装", Usage = "claude update", Category = "core_commands" },
            new() { Id = "cmd_install", Name = "install", Description = "安装 Claude Code", Usage = "claude install stable", Category = "core_commands" },
        };
    }

    private async Task<List<FeishuCommand>> LoadPluginCommandsAsync()
    {
        var commands = new List<FeishuCommand>();

        // 1. 扫描项目技能
        var projectSkillsDir = FeishuPluginPathHelper.GetProjectSkillsDirectory();
        if (Directory.Exists(projectSkillsDir))
        {
            var projectCommands = await ScanSkillsDirectoryAsync(projectSkillsDir, "skills_project");
            commands.AddRange(projectCommands);
        }

        // 2. 扫描全局技能
        var globalSkillsDir = FeishuPluginPathHelper.GetSkillsDirectory();
        if (Directory.Exists(globalSkillsDir))
        {
            var globalCommands = await ScanSkillsDirectoryAsync(globalSkillsDir, "skills_global");
            commands.AddRange(globalCommands);
        }

        // 3. 扫描插件
        var pluginsDir = FeishuPluginPathHelper.GetPluginsDirectory();
        if (Directory.Exists(pluginsDir))
        {
            var pluginCommands = await ScanPluginsDirectoryAsync(pluginsDir);
            commands.AddRange(pluginCommands);
        }

        return commands;
    }

    private async Task<List<FeishuCommand>> ScanSkillsDirectoryAsync(string dir, string category)
    {
        var commands = new List<FeishuCommand>();

        if (!Directory.Exists(dir))
            return commands;

        try
        {
            var subdirs = Directory.GetDirectories(dir);
            foreach (var subdir in subdirs)
            {
                var skillMdPath = Path.Combine(subdir, "SKILL.md");
                if (File.Exists(skillMdPath))
                {
                    var skillName = Path.GetFileName(subdir);
                    var content = await File.ReadAllTextAsync(skillMdPath);
                    var description = ExtractFirstLine(content);

                    commands.Add(new FeishuPluginCommand
                    {
                        Id = $"skill_{category}_{skillName}",
                        Name = $"/{skillName}",
                        Description = description,
                        Usage = $"使用 /{skillName} 调用此技能",
                        Category = category,
                        SkillPath = subdir,
                        IsOfficial = category == "skills_project" && subdir.Contains("claude")
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ [FeishuHelp] 扫描技能目录失败: {Dir}", dir);
        }

        return commands;
    }

    private async Task<List<FeishuCommand>> ScanPluginsDirectoryAsync(string dir)
    {
        var commands = new List<FeishuCommand>();

        if (!Directory.Exists(dir))
            return commands;

        try
        {
            var subdirs = Directory.GetDirectories(dir);
            foreach (var subdir in subdirs)
            {
                var pluginName = Path.GetFileName(subdir);
                commands.Add(new FeishuPluginCommand
                {
                    Id = $"plugin_{pluginName}",
                    Name = pluginName,
                    Description = $"插件: {pluginName}",
                    Usage = $"使用 {pluginName} 插件",
                    Category = "plugins",
                    SkillPath = subdir,
                    IsOfficial = false
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ [FeishuHelp] 扫描插件目录失败: {Dir}", dir);
        }

        return commands;
    }

    private string GetCategoryName(string categoryId)
    {
        return categoryId switch
        {
            "core_debug" => "📋 调试和输出",
            "core_permission" => "🔐 权限和安全",
            "core_session" => "💬 会话管理",
            "core_model" => "🧠 模型选择",
            "core_other" => "⚙️ 其他选项",
            "core_commands" => "📦 管理命令",
            "skills_project" => "📁 项目技能",
            "skills_global" => "🌐 全局技能",
            "plugins" => "🔌 插件",
            _ => categoryId
        };
    }

    private string ExtractFirstLine(string content)
    {
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#') && !trimmed.StartsWith('>'))
            {
                return trimmed.Length > 100 ? trimmed[..100] + "..." : trimmed;
            }
        }
        return "技能命令";
    }
}
```

**Step 2: 验证编译**

Build 项目确保无错误。

---

## Task 4: 创建卡片构建器

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/Channels/FeishuHelpCardBuilder.cs`

**Step 1: 实现 FeishuHelpCardBuilder**

```csharp
using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

public class FeishuHelpCardBuilder
{
    public string BuildCommandListCard(List<FeishuCommandCategory> categories, bool showRefreshButton = true)
    {
        var elements = new List<object>();

        // 更新按钮
        if (showRefreshButton)
        {
            elements.Add(new
            {
                tag = "action",
                actions = new[]
                {
                    new
                    {
                        tag = "button",
                        text = new { tag = "plain_text", content = "🔄 更新命令列表" },
                        type = "default",
                        value = JsonSerializer.Serialize(new { action = "refresh_commands" })
                    }
                }
            });
        }

        // 每个分组一个 overflow
        foreach (var category in categories)
        {
            if (category.Commands.Count == 0)
                continue;

            var options = category.Commands.Select(cmd => new
            {
                text = new { tag = "plain_text", content = cmd.Name },
                value = JsonSerializer.Serialize(new { action = "select_command", command_id = cmd.Id })
            }).ToArray();

            elements.Add(new
            {
                tag = "div",
                text = new { tag = "lark_md", content = $"**{category.Name}**" },
                extra = new
                {
                    tag = "overflow",
                    options = options
                }
            });
        }

        var card = new
        {
            schema = "2.0",
            config = new { enable_forward = true },
            header = new
            {
                template = "blue",
                title = new { tag = "plain_text", content = "🤖 Claude Code CLI 命令帮助" }
            },
            elements = elements
        };

        return JsonSerializer.Serialize(card);
    }

    public string BuildExecuteCard(FeishuCommand command)
    {
        var card = new
        {
            schema = "2.0",
            config = new { enable_forward = true },
            header = new
            {
                template = "purple",
                title = new { tag = "plain_text", content = $"⚙️ 执行命令：{command.Name}" }
            },
            elements = new object[]
            {
                new
                {
                    tag = "div",
                    text = new { tag = "lark_md", content = $"**📝 命令说明：**\n{command.Description}" }
                },
                new
                {
                    tag = "div",
                    text = new { tag = "lark_md", content = $"**💡 用法示例：**\n`{command.Usage}`" }
                },
                new
                {
                    tag = "hr"
                },
                new
                {
                    tag = "form",
                    name = "execute_form",
                    elements = new object[]
                    {
                        new
                        {
                            tag = "input",
                            name = "command_input",
                            placeholder = "编辑命令...",
                            default_value = command.Name.StartsWith("/") ? command.Name : $"claude {command.Name}"
                        },
                        new
                        {
                            tag = "action",
                            actions = new[]
                            {
                                new
                                {
                                    tag = "button",
                                    text = new { tag = "plain_text", content = "❌ 取消" },
                                    type = "default",
                                    action_type = "request",
                                    value = JsonSerializer.Serialize(new { action = "back_to_list" })
                                },
                                new
                                {
                                    tag = "button",
                                    text = new { tag = "plain_text", content = "✅ 执行命令" },
                                    type = "primary",
                                    action_type = "form_submit",
                                    value = JsonSerializer.Serialize(new { action = "execute_command" })
                                }
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(card);
    }

    public string BuildFilteredCard(List<FeishuCommandCategory> categories, string keyword)
    {
        var elements = new List<object>();

        // 过滤说明
        elements.Add(new
        {
            tag = "div",
            text = new { tag = "lark_md", content = $"🔍 搜索结果：**{keyword}**\n\n点击下方按钮返回完整列表" }
        });

        // 返回按钮
        elements.Add(new
        {
            tag = "action",
            actions = new[]
            {
                new
                {
                    tag = "button",
                    text = new { tag = "plain_text", content = "📋 显示全部命令" },
                    type = "default",
                    value = JsonSerializer.Serialize(new { action = "back_to_list" })
                }
            }
        });

        elements.Add(new { tag = "hr" });

        // 匹配的命令（不分组，直接显示）
        var allCommands = categories.SelectMany(c => c.Commands).ToList();
        if (allCommands.Count > 0)
        {
            var options = allCommands.Select(cmd => new
            {
                text = new { tag = "plain_text", content = $"{cmd.Name} - {cmd.Description}" },
                value = JsonSerializer.Serialize(new { action = "select_command", command_id = cmd.Id })
            }).ToArray();

            elements.Add(new
            {
                tag = "div",
                text = new { tag = "lark_md", content = $"**找到 {allCommands.Count} 个匹配命令：**" },
                extra = new
                {
                    tag = "overflow",
                    options = options
                }
            });
        }
        else
        {
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "lark_md", content = "❌ 未找到匹配的命令，请尝试其他关键词" }
            });
        }

        var card = new
        {
            schema = "2.0",
            config = new { enable_forward = true },
            header = new
            {
                template = "turquoise",
                title = new { tag = "plain_text", content = "🔍 命令搜索" }
            },
            elements = elements
        };

        return JsonSerializer.Serialize(card);
    }

    public object BuildToastResponse(string cardJson, string toastMessage, string toastType = "info")
    {
        return new
        {
            toast = new
            {
                type = toastType,
                content = toastMessage
            },
            card = new
            {
                type = "raw",
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(cardJson)
            }
        };
    }

    public object BuildToastOnlyResponse(string toastMessage, string toastType = "info")
    {
        return new
        {
            toast = new
            {
                type = toastType,
                content = toastMessage
            }
        };
    }
}
```

**Step 2: 验证编译**

Build 项目确保无错误。

---

## Task 5: 注册服务到 DI 容器

**Files:**
- Modify: 查找 ServiceCollectionExtensions.cs 或类似文件

**Step 1: 找到服务注册文件**

搜索 `AddFeishuChannel` 或 `services.AddSingleton` 相关代码。

**Step 2: 注册新服务**

在 `AddFeishuChannel` 方法中添加：

```csharp
services.AddSingleton<FeishuCommandService>();
services.AddSingleton<FeishuHelpCardBuilder>();
```

---

## Task 6: 修改 FeishuMessageHandler - 添加 /feishuhelp 检测

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuMessageHandler.cs`

**Step 1: 添加依赖注入**

在构造函数中添加：

```csharp
private readonly FeishuCommandService _commandService;
private readonly FeishuHelpCardBuilder _cardBuilder;
private readonly IFeishuChannelService _channelService;
```

更新构造函数参数，注入这些服务。

**Step 2: 在 HandleMessageReceiveAsync 中添加 /feishuhelp 检测**

在 `var incomingMessage = new FeishuIncomingMessage { ... }` 之前添加：

```csharp
// 检测 /feishuhelp 命令
if (!string.IsNullOrEmpty(content) && content.Trim().StartsWith("/feishuhelp"))
{
    var keyword = content.Trim().Substring("/feishuhelp".Length).Trim();
    await HandleFeishuHelpAsync(message.ChatId, message.MessageId, keyword);
    return;
}
```

**Step 3: 添加 HandleFeishuHelpAsync 方法**

```csharp
private async Task HandleFeishuHelpAsync(string chatId, string replyToMessageId, string keyword)
{
    _logger.LogInformation("🔥 [FeishuHelp] 收到帮助请求: Keyword={Keyword}", keyword);

    try
    {
        List<FeishuCommandCategory> categories;
        string cardJson;

        if (string.IsNullOrEmpty(keyword))
        {
            categories = await _commandService.GetCategorizedCommandsAsync();
            cardJson = _cardBuilder.BuildCommandListCard(categories);
        }
        else
        {
            categories = await _commandService.FilterCommandsAsync(keyword);
            cardJson = _cardBuilder.BuildFilteredCard(categories, keyword);
        }

        // 使用 CardKit 发送卡片
        // 需要调用 FeishuChannelService 或 FeishuCardKitClient
        // 这里假设通过 _channelService 发送
        await _channelService.ReplyCardMessageAsync(replyToMessageId, cardJson);

        _logger.LogInformation("✅ [FeishuHelp] 帮助卡片已发送");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ [FeishuHelp] 发送帮助卡片失败");
    }
}
```

**注意：** 根据现有 `FeishuChannelService` 的实际接口调整发送卡片的方法调用。

---

## Task 7: 添加卡片回调处理

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuMessageHandler.cs` - 或查找卡片回调处理位置
- May need: 创建 IEventHandler for card.action.trigger

**Step 1: 了解现有回调处理方式**

查看 `FeishuMessageHandler` 或其他文件中是否已有 `card.action.trigger` 事件处理。

**Step 2: 添加卡片回调处理器**

根据 FeishuNetSdk 的方式，添加卡片回调处理。如果是通过 IEventHandler 模式，创建新的 Handler：

```csharp
// 示例（根据实际 SDK 调整）
public class FeishuCardActionHandler : IEventHandler<EventV2Dto<CardActionTriggerEvent>, CardActionTriggerEvent>
{
    private readonly FeishuCommandService _commandService;
    private readonly FeishuHelpCardBuilder _cardBuilder;
    private readonly ILogger<FeishuCardActionHandler> _logger;
    // 还需要 CliExecutorService 来执行命令

    public FeishuCardActionHandler(
        FeishuCommandService commandService,
        FeishuHelpCardBuilder cardBuilder,
        ILogger<FeishuCardActionHandler> logger)
    {
        _commandService = commandService;
        _cardBuilder = cardBuilder;
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(EventV2Dto<CardActionTriggerEvent> input, CancellationToken cancellationToken)
    {
        // 解析 action
        // 这里需要根据实际 SDK 的事件结构调整
        return await HandleCardActionAsync(input.Event);
    }

    private async Task<object?> HandleCardActionAsync(CardActionTriggerEvent eventDto)
    {
        try
        {
            // 解析 action.value
            var actionJson = eventDto.Action?.Value?.ToString();
            if (string.IsNullOrEmpty(actionJson))
                return null;

            var action = JsonSerializer.Deserialize<FeishuHelpCardAction>(actionJson);
            if (action == null)
                return null;

            _logger.LogInformation("🔥 [FeishuHelp] 卡片回调: Action={Action}, CommandId={CommandId}",
                action.Action, action.CommandId);

            return action.Action switch
            {
                "refresh_commands" => await HandleRefreshCommandsAsync(),
                "select_command" => await HandleSelectCommandAsync(action.CommandId),
                "back_to_list" => await HandleBackToListAsync(),
                "execute_command" => await HandleExecuteCommandAsync(eventDto),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [FeishuHelp] 处理卡片回调失败");
            return _cardBuilder.BuildToastOnlyResponse("处理失败，请重试", "error");
        }
    }

    private async Task<object> HandleRefreshCommandsAsync()
    {
        await _commandService.RefreshCommandsAsync();
        var categories = await _commandService.GetCategorizedCommandsAsync();
        var cardJson = _cardBuilder.BuildCommandListCard(categories);
        return _cardBuilder.BuildToastResponse(cardJson, "✅ 命令列表已更新", "success");
    }

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

    private async Task<object?> HandleExecuteCommandAsync(CardActionTriggerEvent eventDto)
    {
        // 获取 form_value 中的命令
        var formValue = eventDto.Action?.FormValue;
        if (formValue == null)
            return null;

        // 解析命令输入
        // 这里需要根据实际的 form_value 结构调整
        var commandInput = formValue.TryGetProperty("command_input", out var inputEl)
            ? inputEl.GetString()
            : null;

        if (string.IsNullOrEmpty(commandInput))
        {
            return _cardBuilder.BuildToastOnlyResponse("请输入命令", "warning");
        }

        _logger.LogInformation("🚀 [FeishuHelp] 执行命令: {Command}", commandInput);

        // 返回 toast 确认
        // 实际执行需要通过 FeishuChannelService 触发流式执行
        // 这里复用现有的 CLI 执行逻辑
        return _cardBuilder.BuildToastOnlyResponse("🚀 开始执行命令...", "info");

        // 注意：完整的执行流程需要：
        // 1. 获取 ChatId/Session
        // 2. 调用 CliExecutorService
        // 3. 使用 FeishuStreamingHandle 流式输出
        // 这部分需要与现有 FeishuChannelService 集成
    }
}
```

**Step 3: 注册卡片回调处理器**

在服务注册中添加这个新的 Handler。

---

## Task 8: 集成命令执行（复用现有流式逻辑）

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs` 或类似

**Step 1: 查看现有 FeishuChannelService**

了解现有的消息处理和流式输出逻辑。

**Step 2: 添加从卡片回调触发命令执行的方法**

（这部分根据现有代码结构调整，主要是复用现有的 `CliExecutorService` + `FeishuStreamingHandle` 逻辑）

---

## Task 9: 端到端测试

**Step 1: 启动应用**

确保飞书配置正确，启动应用。

**Step 2: 测试 /feishuhelp**

在飞书中输入 `/feishuhelp`，验证：
- ✅ 收到命令选择卡片
- ✅ 有"更新命令列表"按钮
- ✅ 分组折叠按钮正常

**Step 3: 测试 /feishuhelp <keyword>**

输入 `/feishuhelp debug`，验证：
- ✅ 收到过滤后的卡片
- ✅ 显示"返回全部"按钮

**Step 4: 测试选择命令**

点击某个命令按钮，验证：
- ✅ 卡片更新为执行卡片
- ✅ 输入框预填命令
- ✅ 显示命令说明和示例

**Step 5: 测试取消**

点击"取消"，验证：
- ✅ 返回命令选择卡片

**Step 6: 测试刷新**

点击"更新命令列表"，验证：
- ✅ 显示 toast 成功提示
- ✅ 卡片刷新

---

## Task 10: 错误处理与边界情况测试

**Step 1: 测试不存在的命令 ID**

（模拟错误情况）

**Step 2: 测试空命令输入**

点击"执行命令"但输入框为空

**Step 3: 测试插件目录不存在**

确保不显示对应分组，不报错

---

## 最终检查

- [ ] 所有文件创建/修改完成
- [ ] 编译无错误
- [ ] 端到端测试通过
- [ ] 代码风格与现有代码一致
- [ ] 日志输出完整清晰

---

**Plan complete and saved to `docs/plans/2026-03-02-feishu-slash-command-help-plan.md`. Two execution options:**

**1. Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

**2. Parallel Session (separate)** - Open new session with executing-plans, batch execution with checkpoints

**Which approach?**

