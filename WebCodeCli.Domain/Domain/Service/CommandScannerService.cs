using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;
using Microsoft.Extensions.DependencyInjection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WebCodeCli.Domain.Domain.Service;

[ServiceDescription(typeof(CommandScannerService), ServiceLifetime.Singleton)]
public class CommandScannerService : IDisposable
{
    private readonly ConcurrentDictionary<string, CommandInfo> _commandCache = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly List<string> _scanDirectories = new();
    private bool _disposed = false;

    public CommandScannerService()
    {
        // 初始化扫描目录
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _scanDirectories.Add(Path.Combine(userProfile, ".claude", "skills"));
        _scanDirectories.Add(Path.Combine(userProfile, ".claude", "plugins"));
        _scanDirectories.Add(Path.Combine(AppContext.BaseDirectory, ".claude", "skills"));
    }

    /// <summary>
    /// 初始化服务：首次扫描 + 启动文件监听
    /// </summary>
    public void Initialize()
    {
        ScanAllDirectories();
        StartFileSystemWatchers();
    }

    /// <summary>
    /// 全量扫描所有目录更新缓存
    /// </summary>
    public void ScanAllDirectories()
    {
        _commandCache.Clear();

        foreach (var directory in _scanDirectories.Where(Directory.Exists))
        {
            ScanDirectory(directory);
        }
    }

    /// <summary>
    /// 获取所有命令列表
    /// </summary>
    public List<CommandInfo> GetAllCommands() => _commandCache.Values.ToList();

    /// <summary>
    /// 按分类获取命令列表
    /// </summary>
    public List<CommandInfo> GetCommandsByCategory(string category)
        => _commandCache.Values.Where(c => c.Category == category).ToList();

    private void ScanDirectory(string directory)
    {
        // 扫描所有MD文件
        var mdFiles = Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetFileName(f).EndsWith("COMMAND.md", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetFileName(f).Equals("README.md", StringComparison.OrdinalIgnoreCase));

        foreach (var file in mdFiles)
        {
            try
            {
                var commandInfo = ParseMarkdownDocument(file);
                if (commandInfo != null)
                {
                    _commandCache.AddOrUpdate(commandInfo.Name, commandInfo, (k, v) => commandInfo);
                }
            }
            catch (Exception ex)
            {
                // 记录日志，单个文件解析失败不影响整体
                Console.WriteLine($"解析命令文档失败 {file}: {ex.Message}");
            }
        }
    }

    private CommandInfo? ParseMarkdownDocument(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var commandInfo = new CommandInfo
        {
            SourcePath = filePath,
            LastUpdated = File.GetLastWriteTime(filePath),
            Category = GetCategoryFromPath(filePath)
        };

        // 匹配YAML Front Matter
        var yamlMatch = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n", RegexOptions.Singleline);
        if (yamlMatch.Success)
        {
            try
            {
                var yamlContent = yamlMatch.Groups[1].Value;
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                var frontMatter = deserializer.Deserialize<SkillYamlFrontMatter>(yamlContent);
                commandInfo.Name = frontMatter.Name;
                commandInfo.Description = frontMatter.Description;
                commandInfo.Usage = frontMatter.Usage;

                // 移除YAML部分，继续解析正文
                content = content.Substring(yamlMatch.Length);
            }
            catch
            {
                // YAML解析失败，继续用正文提取
            }
        }

        // 如果没有从YAML获取到名称，从文件名提取
        if (string.IsNullOrWhiteSpace(commandInfo.Name))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            commandInfo.Name = $"/{fileName.ToLowerInvariant().Replace(" ", "-")}";
        }

        // 如果没有从YAML获取到描述，提取正文第一段
        if (string.IsNullOrWhiteSpace(commandInfo.Description))
        {
            var paragraphs = Regex.Split(content, @"\n\s*\n", RegexOptions.Multiline)
                .Where(p => !string.IsNullOrWhiteSpace(p) && !p.StartsWith("#"))
                .ToList();

            if (paragraphs.Any())
            {
                commandInfo.Description = paragraphs.First().Trim().Replace("\n", " ");
            }
        }

        // 提取使用示例：查找所有bash/shell代码块
        if (string.IsNullOrWhiteSpace(commandInfo.Usage))
        {
            var codeBlockMatches = Regex.Matches(content, @"```(bash|shell)\s*\n(.*?)\n```", RegexOptions.Singleline);
            var usages = new List<string>();
            foreach (Match match in codeBlockMatches)
            {
                usages.Add(match.Groups[2].Value.Trim());
            }
            commandInfo.Usage = string.Join("\n", usages.Take(3)); // 最多取3个示例
        }

        // 验证必填字段
        if (string.IsNullOrWhiteSpace(commandInfo.Name) || string.IsNullOrWhiteSpace(commandInfo.Description))
        {
            return null;
        }

        return commandInfo;
    }

    private string GetCategoryFromPath(string filePath)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalSkillsPath = Path.Combine(userProfile, ".claude", "skills").ToLowerInvariant();
        var globalPluginsPath = Path.Combine(userProfile, ".claude", "plugins").ToLowerInvariant();
        var projectSkillsPath = Path.Combine(AppContext.BaseDirectory, ".claude", "skills").ToLowerInvariant();

        var lowerPath = filePath.ToLowerInvariant();

        if (lowerPath.StartsWith(globalSkillsPath)) return "全局技能";
        if (lowerPath.StartsWith(globalPluginsPath)) return "插件命令";
        if (lowerPath.StartsWith(projectSkillsPath)) return "项目技能";

        return "其他命令";
    }

    private void StartFileSystemWatchers()
    {
        // 后续实现
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // 释放文件监听器
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }
            _watchers.Clear();
        }

        _disposed = true;
    }
}
