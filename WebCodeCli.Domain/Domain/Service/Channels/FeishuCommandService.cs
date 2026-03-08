using System.Text.RegularExpressions;
using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
    private readonly IDeserializer _yamlDeserializer;

    public FeishuCommandService(ILogger<FeishuCommandService> logger)
    {
        _logger = logger;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
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

        try
        {
            // 1. 加载核心内置命令
            commands.AddRange(LoadCoreCommands());
            _logger.LogInformation("📥 [FeishuHelp] 加载核心内置命令 {Count} 个", commands.Count);

            // 2. 扫描全局技能
            var globalSkillsDir = FeishuPluginPathHelper.GetSkillsDirectory();
            if (Directory.Exists(globalSkillsDir))
            {
                var globalCommands = await ScanSkillsDirectoryAsync(globalSkillsDir, "skills_global");
                commands.AddRange(globalCommands);
                _logger.LogInformation("📥 [FeishuHelp] 加载全局技能命令 {Count} 个", globalCommands.Count);
            }

            // 3. 扫描项目技能
            var projectSkillsDir = FeishuPluginPathHelper.GetProjectSkillsDirectory();
            if (Directory.Exists(projectSkillsDir))
            {
                var projectCommands = await ScanSkillsDirectoryAsync(projectSkillsDir, "skills_project");
                commands.AddRange(projectCommands);
                _logger.LogInformation("📥 [FeishuHelp] 加载项目技能命令 {Count} 个", projectCommands.Count);
            }

            // 4. 扫描插件
            var pluginsDir = FeishuPluginPathHelper.GetPluginsDirectory();
            if (Directory.Exists(pluginsDir))
            {
                var pluginCommands = await ScanPluginsDirectoryAsync(pluginsDir);
                commands.AddRange(pluginCommands);
                _logger.LogInformation("📥 [FeishuHelp] 加载插件命令 {Count} 个", pluginCommands.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ [FeishuHelp] 加载命令失败");
        }

        // 去重
        commands = commands
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();

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
                // 查找所有可能的命令文档
                var mdFiles = Directory.GetFiles(subdir, "*.md", SearchOption.TopDirectoryOnly)
                    .Where(f => Path.GetFileName(f).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetFileName(f).Equals("COMMAND.md", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetFileName(f).Equals("README.md", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => Path.GetFileName(f) == "SKILL.md" ? 3 :
                                           Path.GetFileName(f) == "COMMAND.md" ? 2 : 1)
                    .ToList();

                foreach (var mdFile in mdFiles)
                {
                    try
                    {
                        var command = await ParseCommandMarkdownAsync(mdFile, category, subdir);
                        if (command != null)
                        {
                            commands.Add(command);
                            break; // 每个技能目录只取优先级最高的一个文档
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ [FeishuHelp] 解析技能文档失败: {File}", mdFile);
                    }
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
    private async Task<List<FeishuCommand>> ScanPluginsDirectoryAsync(string dir)
    {
        var commands = new List<FeishuCommand>();

        if (!Directory.Exists(dir))
            return commands;

        try
        {
            // 扫描所有插件目录下的MD文档
            var mdFiles = Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f).Equals("COMMAND.md", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetFileName(f).Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetFileName(f).EndsWith(".command.md", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var mdFile in mdFiles)
            {
                try
                {
                    var command = await ParseCommandMarkdownAsync(mdFile, "plugins", Path.GetDirectoryName(mdFile)!);
                    if (command != null)
                    {
                        commands.Add(command);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ [FeishuHelp] 解析插件文档失败: {File}", mdFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ [FeishuHelp] 扫描插件目录失败: {Dir}", dir);
        }

        return commands;
    }

    /// <summary>
    /// 解析Markdown命令文档
    /// </summary>
    private async Task<FeishuPluginCommand?> ParseCommandMarkdownAsync(string filePath, string category, string skillPath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var fileName = Path.GetFileName(filePath);
        var skillName = Path.GetFileName(skillPath);

        // 1. 解析Front Matter
        var frontMatter = ParseFrontMatter(content);
        var metadata = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(frontMatter))
        {
            try
            {
                metadata = _yamlDeserializer.Deserialize<Dictionary<string, string>>(frontMatter) ?? new();
            }
            catch
            {
                // YAML解析失败，忽略，继续解析正文
            }
        }

        // 2. 提取命令基本信息
        var commandName = metadata.TryGetValue("name", out var name) ? name : $"/{skillName}";
        if (!commandName.StartsWith("/") && category != "plugins")
        {
            commandName = $"/{commandName}";
        }

        var commandId = $"cmd_{category}_{skillName}_{Path.GetFileNameWithoutExtension(fileName)}".ToLower().Replace(" ", "_");
        var description = metadata.TryGetValue("description", out var desc) ? desc : ExtractDescription(content);
        var usage = metadata.TryGetValue("usage", out var use) ? use : ExtractUsage(content);
        var isOfficial = metadata.TryGetValue("official", out var officialStr) && bool.TryParse(officialStr, out var official) && official;

        // 3. 构建命令对象
        return new FeishuPluginCommand
        {
            Id = commandId,
            Name = commandName,
            Description = description,
            Usage = usage,
            Category = category,
            SkillPath = skillPath,
            IsOfficial = isOfficial
        };
    }

    /// <summary>
    /// 解析Markdown的Front Matter
    /// </summary>
    private string? ParseFrontMatter(string content)
    {
        var frontMatterRegex = new Regex(@"^---\s*\n([\s\S]*?)\n---\s*\n", RegexOptions.Multiline);
        var match = frontMatterRegex.Match(content);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// 提取命令描述
    /// </summary>
    private string ExtractDescription(string content)
    {
        // 去掉Front Matter
        var contentWithoutFrontMatter = Regex.Replace(content, @"^---\s*\n[\s\S]*?\n---\s*\n", "");

        // 查找第一个非标题、非注释的段落
        var lines = contentWithoutFrontMatter.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed) &&
                !trimmed.StartsWith('#') &&
                !trimmed.StartsWith('>') &&
                !trimmed.StartsWith("```") &&
                !trimmed.StartsWith('|') &&
                !trimmed.StartsWith('-') &&
                !trimmed.StartsWith('*'))
            {
                return trimmed.Length > 150 ? trimmed[..150] + "..." : trimmed;
            }
        }

        return "Claude CLI 命令";
    }

    /// <summary>
    /// 提取使用示例
    /// </summary>
    private string ExtractUsage(string content)
    {
        // 查找代码块中的使用示例
        var codeBlockRegex = new Regex(@"```(?:bash|shell|cli)?\s*\n([\s\S]*?)\n```", RegexOptions.Multiline);
        var matches = codeBlockRegex.Matches(content);

        foreach (Match match in matches)
        {
            var code = match.Groups[1].Value.Trim();
            if (code.StartsWith("/") || code.StartsWith("claude "))
            {
                // 取第一个符合的示例
                return code.Split('\n').First().Trim();
            }
        }

        // 查找"用法"、"示例"、"使用"等章节
        var usageRegex = new Regex(@"##\s*(?:用法|使用|示例|Example|Usage)\s*\n([\s\S]*?)(?=\n##|\Z)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var usageMatch = usageRegex.Match(content);
        if (usageMatch.Success)
        {
            var usageContent = usageMatch.Groups[1].Value.Trim();
            var lines = usageContent.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l));
            foreach (var line in lines)
            {
                if (line.StartsWith("/") || line.StartsWith("claude ") || line.StartsWith("`/") || line.StartsWith("`claude "))
                {
                    return line.Trim('`', ' ');
                }
            }
        }

        // 没有找到示例，返回默认用法
        var skillName = Path.GetFileName(Path.GetDirectoryName(content)) ?? "command";
        return skillName.StartsWith("/") ? skillName : $"/{skillName}";
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
            "plugins" => "🔌 插件命令",
            _ => categoryId
        };
    }
}
