namespace WebCodeCli.Domain.Domain.Model.Channels;

/// <summary>
/// 飞书命令分组模型
/// 将相关命令组织在一起显示
/// </summary>
public class FeishuCommandCategory
{
    /// <summary>
    /// 分组ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称（包含emoji）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 该分组下的命令列表
    /// </summary>
    public List<FeishuCommand> Commands { get; set; } = new();
}
