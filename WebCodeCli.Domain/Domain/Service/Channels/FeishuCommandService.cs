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
    /// 按分组组织命令（仅保留需要的分组）
    /// </summary>
    public async Task<List<FeishuCommandCategory>> GetCategorizedCommandsAsync()
    {
        // 只保留需要的分组
        var allowedCategories = new[] { "core_session", "skills_project", "skills_global", "plugins" };

        var commands = await GetCommandsAsync();
        return commands
            .Where(c => allowedCategories.Contains(c.Category))
            .GroupBy(c => c.Category)
            .Select(g => new FeishuCommandCategory
            {
                Id = g.Key,
                Name = GetCategoryName(g.Key),
                Commands = g.ToList()
            })
            // 按指定顺序排序
            .OrderBy(c => Array.IndexOf(allowedCategories, c.Id))
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

        // 只保留需要的分组
        var allowedCategories = new[] { "core_session", "skills_project", "skills_global", "plugins" };

        var commands = await GetCommandsAsync();
        var lowerKeyword = keyword.ToLowerInvariant();
        var filtered = commands.Where(c =>
            allowedCategories.Contains(c.Category) &&
            (c.Name.ToLowerInvariant().Contains(lowerKeyword) ||
            c.Description.ToLowerInvariant().Contains(lowerKeyword) ||
            c.Id.ToLowerInvariant().Contains(lowerKeyword)))
            .ToList();

        return filtered
            .GroupBy(c => c.Category)
            .Select(g => new FeishuCommandCategory
            {
                Id = g.Key,
                Name = GetCategoryName(g.Key),
                Commands = g.ToList()
            })
            // 按指定顺序排序
            .OrderBy(c => Array.IndexOf(allowedCategories, c.Id))
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
            // 会话管理
            new() { Id = "feishusessions", Name = "/feishusessions", Description = "飞书会话管理", Usage = "发送 /feishusessions 打开会话管理卡片，支持切换、关闭、新建会话", Category = "core_session" },
            new() { Id = "init", Name = "/init", Description = "初始化当前会话工作环境", Usage = "发送 /init 自动检测并初始化项目环境，安装依赖", Category = "core_session" },
            new() { Id = "clear", Name = "/clear", Description = "清空当前会话上下文", Usage = "发送 /clear 清空当前会话的聊天历史和上下文记忆，不关闭会话", Category = "core_session" },
            new() { Id = "compact", Name = "/compact", Description = "压缩会话历史", Usage = "发送 /compact 压缩当前会话历史记录，减少内存占用", Category = "core_session" }
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
            "core_session" => "📋 会话管理",
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
