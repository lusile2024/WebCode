using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;
using Microsoft.Extensions.Logging;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书命令服务
/// 管理所有可用的CLI命令和技能
/// 支持缓存、刷新、过滤等操作
/// </summary>
public class FeishuCommandService
{
    private static List<FeishuCommand>? _cachedCommands;
    private static readonly object _lock = new();
    private readonly ILogger<FeishuCommandService> _logger;

    public FeishuCommandService(ILogger<FeishuCommandService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取命令列表（带缓存）
    /// </summary>
    public async Task<List<FeishuCommand>> GetCommandsAsync()
    {
        if (_cachedCommands == null)
        {
            await LoadCommandsAsync();
        }
        return _cachedCommands ?? new List<FeishuCommand>();
    }

    /// <summary>
    /// 刷新命令列表
    /// </summary>
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

    /// <summary>
    /// 根据ID获取单个命令
    /// </summary>
    public async Task<FeishuCommand?> GetCommandAsync(string commandId)
    {
        var commands = await GetCommandsAsync();
        return commands.FirstOrDefault(c => c.Id == commandId);
    }

    /// <summary>
    /// 按分组组织命令
    /// </summary>
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

    /// <summary>
    /// 过滤命令（支持关键字搜索）
    /// </summary>
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

    /// <summary>
    /// 加载所有命令
    /// </summary>
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

    /// <summary>
    /// 加载核心命令
    /// </summary>
    private List<FeishuCommand> LoadCoreCommands()
    {
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

    /// <summary>
    /// 加载插件命令
    /// </summary>
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

    /// <summary>
    /// 扫描技能目录
    /// </summary>
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

    /// <summary>
    /// 扫描插件目录
    /// </summary>
    private Task<List<FeishuCommand>> ScanPluginsDirectoryAsync(string dir)
    {
        var commands = new List<FeishuCommand>();

        if (!Directory.Exists(dir))
            return Task.FromResult(commands);

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

        return Task.FromResult(commands);
    }

    /// <summary>
    /// 获取分组显示名称
    /// </summary>
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

    /// <summary>
    /// 提取第一行非注释行作为描述
    /// </summary>
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
