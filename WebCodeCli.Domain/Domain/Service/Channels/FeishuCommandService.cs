using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using Microsoft.Extensions.Logging;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书命令服务
/// 按 CLI 工具聚合内置命令、技能和插件命令
/// </summary>
public class FeishuCommandService
{
    private readonly ILogger<FeishuCommandService> _logger;
    private readonly CommandScannerService _commandScannerService;
    private readonly Dictionary<string, List<FeishuCommand>> _cachedCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public FeishuCommandService(ILogger<FeishuCommandService> logger, CommandScannerService commandScannerService)
    {
        _logger = logger;
        _commandScannerService = commandScannerService;
    }

    public async Task<List<FeishuCommand>> GetCommandsAsync(string? toolId = null)
    {
        var normalizedToolId = NormalizeToolId(toolId);
        lock (_lock)
        {
            if (_cachedCommands.TryGetValue(normalizedToolId, out var cached))
            {
                return cached.ToList();
            }
        }

        await LoadCommandsAsync(normalizedToolId);

        lock (_lock)
        {
            return _cachedCommands.TryGetValue(normalizedToolId, out var cached)
                ? cached.ToList()
                : new List<FeishuCommand>();
        }
    }

    public async Task RefreshCommandsAsync(string? toolId = null)
    {
        var normalizedToolId = NormalizeToolId(toolId);
        _logger.LogInformation("🔄 [FeishuHelp] 刷新命令列表, ToolId={ToolId}", normalizedToolId);

        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(normalizedToolId))
            {
                _cachedCommands.Clear();
            }
            else
            {
                _cachedCommands.Remove(normalizedToolId);
            }
        }

        await LoadCommandsAsync(normalizedToolId);

        lock (_lock)
        {
            var count = _cachedCommands.TryGetValue(normalizedToolId, out var cached) ? cached.Count : 0;
            _logger.LogInformation("✅ [FeishuHelp] 命令列表已刷新, ToolId={ToolId}, Count={Count}", normalizedToolId, count);
        }
    }

    public async Task<FeishuCommand?> GetCommandAsync(string commandId, string? toolId = null)
    {
        var commands = await GetCommandsAsync(toolId);
        return commands.FirstOrDefault(c => c.Id.Equals(commandId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<FeishuCommandCategory>> GetCategorizedCommandsAsync(string? toolId = null)
    {
        var commands = await GetCommandsAsync(toolId);
        return commands
            .GroupBy(c => c.Category)
            .Select(g => new FeishuCommandCategory
            {
                Id = g.Key,
                Name = GetCategoryName(g.Key),
                Commands = g.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .OrderBy(c => GetCategoryOrder(c.Id))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<FeishuCommandCategory>> FilterCommandsAsync(string keyword, string? toolId = null)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return await GetCategorizedCommandsAsync(toolId);
        }

        var commands = await GetCommandsAsync(toolId);
        var lowerKeyword = keyword.ToLowerInvariant();
        var filtered = commands.Where(c =>
                c.Name.ToLowerInvariant().Contains(lowerKeyword)
                || c.Description.ToLowerInvariant().Contains(lowerKeyword)
                || c.Id.ToLowerInvariant().Contains(lowerKeyword)
                || c.ExecuteText.ToLowerInvariant().Contains(lowerKeyword))
            .ToList();

        return filtered
            .GroupBy(c => c.Category)
            .Select(g => new FeishuCommandCategory
            {
                Id = g.Key,
                Name = GetCategoryName(g.Key),
                Commands = g.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .OrderBy(c => GetCategoryOrder(c.Id))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Task LoadCommandsAsync(string normalizedToolId)
    {
        lock (_lock)
        {
            if (_cachedCommands.ContainsKey(normalizedToolId))
            {
                return Task.CompletedTask;
            }
        }

        var commands = new List<FeishuCommand>();
        commands.AddRange(GetFeishuBuiltInCommands(normalizedToolId));
        commands.AddRange(GetToolBuiltInCommands(normalizedToolId));

        foreach (var cmd in _commandScannerService.GetAllCommands(normalizedToolId))
        {
            commands.Add(new FeishuCommand
            {
                ToolId = normalizedToolId,
                Id = cmd.Name.TrimStart('/', '$'),
                Name = cmd.Name.StartsWith("/") || cmd.Name.StartsWith("$") ? cmd.Name : cmd.Invocation,
                Description = cmd.Description,
                Usage = string.IsNullOrWhiteSpace(cmd.Usage) ? cmd.Invocation : cmd.Usage,
                ExecuteText = string.IsNullOrWhiteSpace(cmd.Invocation) ? cmd.Name : cmd.Invocation,
                Category = cmd.Category
            });
        }

        var deduplicated = commands
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(GetCommandPriority).First())
            .OrderBy(c => GetCategoryOrder(c.Category))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_lock)
        {
            _cachedCommands[normalizedToolId] = deduplicated;
        }

        return Task.CompletedTask;
    }

    private static int GetCommandPriority(FeishuCommand command)
    {
        return command.Category switch
        {
            "feishu_builtin" => 0,
            "core_builtin" => 1,
            "skills_project" => 2,
            "commands_project" => 3,
            "skills_global" => 4,
            "commands_global" => 5,
            "plugins" => 6,
            _ => 10
        };
    }

    private static string NormalizeToolId(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return "claude-code";
        }

        if (toolId.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            return "claude-code";
        }

        if (toolId.Equals("opencode-cli", StringComparison.OrdinalIgnoreCase))
        {
            return "opencode";
        }

        return toolId;
    }

    private static List<FeishuCommand> GetFeishuBuiltInCommands(string toolId)
    {
        return new List<FeishuCommand>
        {
            new()
            {
                ToolId = toolId,
                Id = "feishuhelp",
                Name = "/feishuhelp",
                Description = "显示当前工具可用的内置命令、技能和插件命令",
                Usage = "/feishuhelp [关键词]",
                ExecuteText = "/feishuhelp",
                Category = "feishu_builtin"
            },
            new()
            {
                ToolId = toolId,
                Id = "feishusessions",
                Name = "/feishusessions",
                Description = "查看、切换和创建当前飞书聊天绑定的会话",
                Usage = "/feishusessions",
                ExecuteText = "/feishusessions",
                Category = "feishu_builtin"
            },
            new()
            {
                ToolId = toolId,
                Id = "feishuprojects",
                Name = "/feishuprojects",
                Description = "查看、创建、克隆、拉取和删除当前飞书聊天绑定的项目",
                Usage = "/feishuprojects",
                ExecuteText = "/feishuprojects",
                Category = "feishu_builtin"
            }
        };
    }

    private static IEnumerable<FeishuCommand> GetToolBuiltInCommands(string toolId)
    {
        return toolId switch
        {
            "claude-code" => new[]
            {
                BuildToolBuiltIn(toolId, "history", "/history", "查看当前 CLI 会话的最近历史消息"),
                BuildToolBuiltIn(toolId, "init", "/init", "初始化当前项目的上下文与约定"),
                BuildToolBuiltIn(toolId, "clear", "/clear", "清空当前会话上下文"),
                BuildToolBuiltIn(toolId, "compact", "/compact", "压缩当前会话上下文，减少 token 占用")
            },
            "codex" => new[]
            {
                BuildToolBuiltIn(toolId, "history", "/history", "查看当前 CLI 会话的最近历史消息"),
                BuildToolBuiltIn(toolId, "init", "/init", "初始化当前项目的上下文与约定"),
                BuildToolBuiltIn(toolId, "help", "/help", "显示 Codex 交互命令帮助"),
                BuildToolBuiltIn(toolId, "status", "/status", "查看当前会话状态和工作区信息"),
                BuildToolBuiltIn(toolId, "approvals", "/approvals", "查看或调整当前审批策略"),
                BuildToolBuiltIn(toolId, "model", "/model", "查看或切换当前模型"),
                BuildToolBuiltIn(toolId, "reasoning", "/reasoning", "查看或调整推理强度")
            },
            "opencode" => new[]
            {
                BuildToolBuiltIn(toolId, "history", "/history", "查看当前 CLI 会话的最近历史消息"),
                BuildToolBuiltIn(toolId, "init", "/init", "为当前项目初始化 OpenCode 工作上下文"),
                BuildToolBuiltIn(toolId, "help", "/help", "显示 OpenCode 交互命令帮助"),
                BuildToolBuiltIn(toolId, "undo", "/undo", "回滚上一条交互产生的变更"),
                BuildToolBuiltIn(toolId, "redo", "/redo", "恢复最近一次撤销的变更"),
                BuildToolBuiltIn(toolId, "share", "/share", "分享当前会话")
            },
            _ => Array.Empty<FeishuCommand>()
        };
    }

    private static FeishuCommand BuildToolBuiltIn(string toolId, string id, string name, string description)
    {
        return new FeishuCommand
        {
            ToolId = toolId,
            Id = id,
            Name = name,
            Description = description,
            Usage = name,
            ExecuteText = name,
            Category = "core_builtin"
        };
    }

    private static int GetCategoryOrder(string categoryId)
    {
        return categoryId switch
        {
            "feishu_builtin" => 0,
            "core_builtin" => 1,
            "skills_project" => 2,
            "skills_global" => 3,
            "commands_project" => 4,
            "commands_global" => 5,
            "plugins" => 6,
            _ => 99
        };
    }

    private string GetCategoryName(string categoryId)
    {
        return categoryId switch
        {
            "feishu_builtin" => "🤖 飞书命令",
            "core_builtin" => "📦 内置命令",
            "skills_project" => "📁 项目技能",
            "skills_global" => "🌐 全局技能",
            "commands_project" => "🧩 项目命令",
            "commands_global" => "🛠️ 全局命令",
            "plugins" => "🔌 插件",
            _ => categoryId
        };
    }
}
