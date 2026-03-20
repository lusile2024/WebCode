namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// 命令元数据
/// </summary>
public class CommandInfo
{
    /// <summary>
    /// 归属的 CLI 工具 ID（如 claude-code / codex / opencode）
    /// </summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// 命令名称（如 /feishuhelp）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 实际建议执行的文本
    /// 对于技能，通常会补成 $skill-name；对于内置命令则与 Name 相同
    /// </summary>
    public string Invocation { get; set; } = string.Empty;

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
