using System.Collections.Concurrent;
using System.IO;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;
using Microsoft.Extensions.DependencyInjection;

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
        // 后续实现，先返回null
        return null;
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
