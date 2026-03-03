namespace WebCodeCli.Domain.Domain.Model.Channels;

/// <summary>
/// 飞书命令模型
/// 表示一个可用的CLI命令或技能
/// </summary>
public class FeishuCommand
{
    /// <summary>
    /// 唯一标识，如 "help"
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称，如 "--help"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 简短描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 用法示例
    /// </summary>
    public string Usage { get; set; } = string.Empty;

    /// <summary>
    /// 所属分组
    /// </summary>
    public string Category { get; set; } = string.Empty;
}

/// <summary>
/// 飞书插件/技能命令模型
/// 扩展基础命令，添加插件特定信息
/// </summary>
public class FeishuPluginCommand : FeishuCommand
{
    /// <summary>
    /// 插件/技能路径
    /// </summary>
    public string SkillPath { get; set; } = string.Empty;

    /// <summary>
    /// 是否官方插件
    /// </summary>
    public bool IsOfficial { get; set; }
}
