# 飞书命令自动扫描功能设计文档

## 需求概述
替换原有飞书 `/feishuhelp` 命令的硬编码命令列表，实现自动扫描系统中所有可用的技能和插件命令，动态生成帮助卡片。

## 技术方案
采用**文件系统监听+内存缓存**方案，兼顾实时性和响应速度。

### 整体架构
```
┌─────────────────┐     ┌──────────────────────┐     ┌────────────────────┐
│  飞书消息事件   │────▶│  FeishuMessageHandler │────▶│ FeishuCommandService │
└─────────────────┘     └──────────────────────┘     └────────────────────┘
                                                             ▲
                                                             │
┌─────────────────┐     ┌──────────────────────┐            │
│ 文件系统监听器  │────▶│  CommandScannerService│────────────┘
└─────────────────┘     └──────────────────────┘
                                                             ▲
                                                             │
┌─────────────────┐     ┌──────────────────────┐            │
│  卡片动作事件   │────▶│ FeishuCardActionHandler│────────────┘
└─────────────────┘     └──────────────────────┘
```

## 核心组件设计

### 1. 数据模型
```csharp
/// <summary>
/// 命令元数据
/// </summary>
public class CommandInfo
{
    /// <summary>
    /// 命令名称（如 /feishuhelp）
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// 命令描述
    /// </summary>
    public string Description { get; set; }
    
    /// <summary>
    /// 使用示例
    /// </summary>
    public string Usage { get; set; }
    
    /// <summary>
    /// 命令分类：全局技能/项目技能/插件命令
    /// </summary>
    public string Category { get; set; }
    
    /// <summary>
    /// 来源路径（MD文档路径）
    /// </summary>
    public string SourcePath { get; set; }
    
    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// YAML Front Matter 解析模型
/// </summary>
public class SkillYamlFrontMatter
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Usage { get; set; }
    public string[] Alias { get; set; }
    public string Category { get; set; }
}
```

### 2. CommandScannerService 核心服务
- 单例服务，生命周期：Singleton
- 职责：目录扫描、MD文档解析、命令缓存、文件系统监听
- 核心API：
  - `Initialize()` - 服务初始化，首次扫描+启动监听
  - `ScanAllDirectories()` - 全量扫描所有目录更新缓存
  - `GetAllCommands()` - 获取所有命令列表
  - `GetCommandsByCategory(string category)` - 按分类获取命令

### 3. Markdown 解析规则
1. **YAML Front Matter 优先**：匹配文档开头 `---` 和 `---` 之间的内容，使用 YamlDotNet 解析
2. **命令名称**：优先 YAML name 字段，其次从文件名提取
3. **描述**：优先 YAML description 字段，其次提取正文第一段
4. **使用示例**：提取文档中所有 `bash`/`shell` 类型的代码块
5. **分类**：按目录自动划分：
   - `~/.claude/skills/**` → 全局技能
   - `项目目录/.claude/skills/**` → 项目技能
   - `~/.claude/plugins/**` → 插件命令

## 工作流程
1. 应用启动时，`CommandScannerService` 初始化，执行首次全量扫描，启动文件系统监听
2. 当 `~/.claude/skills`、`~/.claude/plugins` 或项目级 skills 目录有文件变更时，自动增量更新缓存
3. 用户发送 `/feishuhelp` 命令时，直接从缓存获取命令列表生成卡片返回
4. 用户点击"更新命令列表"按钮时，触发全量扫描，更新缓存后返回最新列表

## 错误处理
- 单个文件解析失败：记录日志，跳过该文件，不影响整体流程
- 目录不存在：自动跳过，记录警告
- 文件访问权限不足：跳过，记录警告
- 文件监听异常：自动重试，最多重试3次，失败则降级为定时扫描模式

## 性能指标
- 命令查询响应时间：< 10ms（缓存命中）
- 全量扫描时间：< 1s（100个文档以内）
- 增量更新延迟：< 500ms（文件变更后到缓存更新）
