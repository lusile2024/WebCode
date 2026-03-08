# 飞书命令自动扫描功能 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 实现飞书 `/feishuhelp` 命令自动扫描系统所有技能和插件命令，动态生成帮助卡片，替换原有硬编码实现。

**Architecture:** 使用单例 `CommandScannerService` 提供目录扫描、Markdown文档解析、命令信息缓存和文件系统监听功能，集成到现有飞书消息处理和卡片动作处理流程中，兼顾实时性和响应速度。

**Tech Stack:** .NET 10.0, YamlDotNet 16.3.0, FileSystemWatcher, 领域驱动设计模式

---

### Task 1: 添加 YamlDotNet 依赖

**Files:**
- Modify: `WebCodeCli.Domain/WebCodeCli.Domain.csproj`

**Step 1: 添加 NuGet 包引用**
```xml
<PackageReference Include="YamlDotNet" Version="16.3.0" />
```

**Step 2: 恢复 NuGet 包**
Run: `dotnet restore WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 依赖恢复成功，无错误

**Step 3: 提交变更**
```bash
git add WebCodeCli.Domain/WebCodeCli.Domain.csproj
git commit -m "feat: 添加 YamlDotNet 依赖用于解析YAML Front Matter"
```

---

### Task 2: 创建数据模型

**Files:**
- Create: `WebCodeCli.Domain/Domain/Model/CommandInfo.cs`
- Create: `WebCodeCli.Domain/Domain/Model/SkillYamlFrontMatter.cs`

**Step 1: 编写 CommandInfo 模型**
```csharp
namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// 命令元数据
/// </summary>
public class CommandInfo
{
    /// <summary>
    /// 命令名称（如 /feishuhelp）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 命令描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 使用示例
    /// </summary>
    public string Usage { get; set; } = string.Empty;

    /// <summary>
    /// 命令分类：全局技能/项目技能/插件命令
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 来源路径（MD文档路径）
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdated { get; set; }
}
```

**Step 2: 编写 SkillYamlFrontMatter 模型**
```csharp
namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// 技能文档YAML Front Matter解析模型
/// </summary>
public class SkillYamlFrontMatter
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;
    public string[] Alias { get; set; } = Array.Empty<string>();
    public string Category { get; set; } = string.Empty;
}
```

**Step 3: 编译项目验证模型正确性**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功，无错误

**Step 4: 提交变更**
```bash
git add WebCodeCli.Domain/Domain/Model/CommandInfo.cs WebCodeCli.Domain/Domain/Model/SkillYamlFrontMatter.cs
git commit -m "feat: 添加命令扫描功能数据模型"
```

---

### Task 3: 实现 CommandScannerService 基础框架

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/CommandScannerService.cs`

**Step 1: 编写服务基础框架**
```csharp
using System.Collections.Concurrent;
using System.IO;
using WebCodeCli.Domain.Common;
using WebCodeCli.Domain.Domain.Model;
using Microsoft.Extensions.DependencyInjection;

namespace WebCodeCli.Domain.Domain.Service;

[ServiceDescription(Lifetime = ServiceLifetime.Singleton)]
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
        // 后续实现
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
```

**Step 2: 编译验证**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功

**Step 3: 提交变更**
```bash
git add WebCodeCli.Domain/Domain/Service/CommandScannerService.cs
git commit -m "feat: 实现 CommandScannerService 基础框架"
```

---

### Task 4: 实现 Markdown 文档解析功能

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/CommandScannerService.cs`

**Step 1: 实现 ParseMarkdownDocument 方法**
```csharp
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text.RegularExpressions;

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
```

**Step 2: 编译验证**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功

**Step 3: 提交变更**
```bash
git add WebCodeCli.Domain/Domain/Service/CommandScannerService.cs
git commit -m "feat: 实现 Markdown 文档解析功能"
```

---

### Task 5: 实现文件系统监听功能

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/CommandScannerService.cs`

**Step 1: 实现 StartFileSystemWatchers 方法**
```csharp
private void StartFileSystemWatchers()
{
    foreach (var directory in _scanDirectories.Where(Directory.Exists))
    {
        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                Filter = "*.md",
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Created += OnFileChanged;
            watcher.Changed += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileRenamed;

            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"启动目录监听失败 {directory}: {ex.Message}");
        }
    }
}

private void OnFileChanged(object sender, FileSystemEventArgs e)
{
    try
    {
        // 防抖处理：延迟500ms处理，避免频繁修改导致多次扫描
        Task.Delay(500).Wait();

        if (File.Exists(e.FullPath))
        {
            // 文件新增或修改，重新解析
            var commandInfo = ParseMarkdownDocument(e.FullPath);
            if (commandInfo != null)
            {
                _commandCache.AddOrUpdate(commandInfo.Name, commandInfo, (k, v) => commandInfo);
            }
        }
        else
        {
            // 文件删除，从缓存移除
            var toRemove = _commandCache.Where(c => c.Value.SourcePath.Equals(e.FullPath, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var item in toRemove)
            {
                _commandCache.TryRemove(item.Key, out _);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"处理文件变更失败 {e.FullPath}: {ex.Message}");
    }
}

private void OnFileRenamed(object sender, RenamedEventArgs e)
{
    // 先删除旧文件
    var toRemove = _commandCache.Where(c => c.Value.SourcePath.Equals(e.OldFullPath, StringComparison.OrdinalIgnoreCase)).ToList();
    foreach (var item in toRemove)
    {
        _commandCache.TryRemove(item.Key, out _);
    }

    // 再处理新文件
    OnFileChanged(sender, new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(e.FullPath)!, Path.GetFileName(e.FullPath)));
}
```

**Step 2: 编译验证**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功

**Step 3: 提交变更**
```bash
git add WebCodeCli.Domain/Domain/Service/CommandScannerService.cs
git commit -m "feat: 实现文件系统监听和增量更新功能"
```

---

### Task 6: 集成到 FeishuCommandService

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCommandService.cs`

**Step 1: 注入 CommandScannerService 依赖**
```csharp
// 在类构造函数中添加
private readonly CommandScannerService _commandScannerService;

public FeishuCommandService(CommandScannerService commandScannerService)
{
    _commandScannerService = commandScannerService;
    // 其他原有构造逻辑
}
```

**Step 2: 实现 GetHelpCommands 方法**
```csharp
/// <summary>
/// 获取所有帮助命令列表
/// </summary>
public List<CommandInfo> GetHelpCommands()
{
    return _commandScannerService.GetAllCommands();
}

/// <summary>
/// 强制刷新命令列表
/// </summary>
public void RefreshCommands()
{
    _commandScannerService.ScanAllDirectories();
}
```

**Step 3: 删除原有硬编码命令列表**
删除类中所有硬编码的 `_commands` 字段和相关初始化逻辑。

**Step 4: 编译验证**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功

**Step 5: 提交变更**
```bash
git add WebCodeCli.Domain/Domain/Service/Channels/FeishuCommandService.cs
git commit -m "feat: 集成 CommandScannerService 到 FeishuCommandService"
```

---

### Task 7: 修改 /feishuhelp 命令处理逻辑

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuMessageHandler.cs`

**Step 1: 修改 /feishuhelp 命令处理**
```csharp
// 找到处理 /feishuhelp 命令的位置，替换原有逻辑
if (message.Text.Trim().Equals("/feishuhelp", StringComparison.OrdinalIgnoreCase))
{
    // 自动刷新命令列表
    _feishuCommandService.RefreshCommands();

    var commands = _feishuCommandService.GetHelpCommands();
    // 原有生成卡片逻辑，使用新的 commands 列表
    var card = BuildHelpCard(commands);
    await ReplyMessage(message, card);
    return;
}
```

**Step 2: 编译验证**
Run: `dotnet build WebCodeCli.sln`
Expected: 编译成功

**Step 3: 提交变更**
```bash
git add WebCodeCli.Domain/Domain/Service/Channels/FeishuMessageHandler.cs
git commit -m "feat: 修改 /feishuhelp 命令自动刷新命令列表"
```

---

### Task 8: 保留"更新命令列表"按钮功能

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuMessageHandler.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionHandler.cs`

**Step 1: 修改卡片动作处理逻辑**
```csharp
// 找到处理"更新命令列表"按钮的action
if (action.ActionValue == "refresh_commands")
{
    _feishuCommandService.RefreshCommands();
    var commands = _feishuCommandService.GetHelpCommands();
    var card = BuildHelpCard(commands);
    await ReplyCard(action, card, replaceOriginal: true);
    return;
}
```

**Step 2: 编译验证**
Run: `dotnet build WebCodeCli.sln`
Expected: 编译成功

**Step 3: 提交变更**
```bash
git add WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionHandler.cs
git commit -m "feat: 保留更新命令列表按钮功能"
```

---

### Task 9: 服务注册和初始化

**Files:**
- Modify: `WebCodeCli/Program.cs`

**Step 1: 服务启动时初始化 CommandScannerService**
```csharp
// 在 Build() 之后，Run() 之前添加
var serviceProvider = builder.Build();

// 初始化命令扫描服务
using (var scope = serviceProvider.CreateScope())
{
    var commandScanner = scope.ServiceProvider.GetRequiredService<CommandScannerService>();
    commandScanner.Initialize();
}

await serviceProvider.RunAsync();
```

**Step 2: 编译验证**
Run: `dotnet build WebCodeCli.sln`
Expected: 编译成功

**Step 3: 提交变更**
```bash
git add WebCodeCli/Program.cs
git commit -m "feat: 启动时初始化命令扫描服务"
```

---

### Task 10: 功能测试验证

**Step 1: 运行应用**
Run: `dotnet run --project WebCodeCli`
Expected: 应用启动成功，无错误

**Step 2: 功能验证**
1. 向飞书机器人发送 `/feishuhelp` 命令，验证返回卡片是否包含所有技能和插件命令
2. 添加新的技能MD文件到 `~/.claude/skills` 目录，再次发送 `/feishuhelp`，验证新命令是否自动出现
3. 点击卡片上的"更新命令列表"按钮，验证命令列表是否正确刷新

**Step 3: 提交测试完成**
```bash
git commit --allow-empty -m "test: 飞书命令自动扫描功能测试通过"
```
