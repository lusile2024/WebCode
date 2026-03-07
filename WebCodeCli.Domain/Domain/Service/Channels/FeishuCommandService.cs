using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
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
    private readonly CommandScannerService _commandScannerService;

    public FeishuCommandService(ILogger<FeishuCommandService> logger, CommandScannerService commandScannerService)
    {
        _logger = logger;
        _commandScannerService = commandScannerService;
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

        // 从 CommandScannerService 获取所有命令
        var scannerCommands = _commandScannerService.GetAllCommands();

        foreach (var cmd in scannerCommands)
        {
            commands.Add(new FeishuCommand
            {
                Id = cmd.Name.TrimStart('/'),
                Name = cmd.Name,
                Description = cmd.Description,
                Usage = cmd.Usage,
                Category = MapCategory(cmd.Category)
            });
        }

        lock (_lock)
        {
            _cachedCommands = commands;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 映射分类到现有分类体系
    /// </summary>
    private string MapCategory(string category)
    {
        return category switch
        {
            "全局技能" => "skills_global",
            "项目技能" => "skills_project",
            "插件命令" => "plugins",
            _ => "core_other"
        };
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

}
