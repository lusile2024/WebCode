using SqlSugar;

namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// CLI 工具环境变量配置方案（支持多套 AI 配置快速切换）
/// </summary>
[SugarTable("cli_tool_env_profiles")]
public class CliToolEnvProfile
{
    /// <summary>
    /// 主键 ID
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// CLI 工具 ID
    /// </summary>
    [SugarColumn(Length = 50, IsNullable = false, ColumnDescription = "CLI工具ID")]
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// 方案名称（如 "OpenAI", "DeepSeek", "Anthropic"）
    /// </summary>
    [SugarColumn(Length = 100, IsNullable = false, ColumnDescription = "方案名称")]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>
    /// 是否为当前激活方案
    /// </summary>
    [SugarColumn(IsNullable = false, ColumnDescription = "是否激活")]
    public bool IsActive { get; set; } = false;

    /// <summary>
    /// 环境变量（JSON 格式存储键值对）
    /// </summary>
    [SugarColumn(Length = 8000, IsNullable = true, ColumnDescription = "环境变量JSON")]
    public string? EnvVarsJson { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [SugarColumn(IsNullable = false, ColumnDescription = "创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    [SugarColumn(IsNullable = false, ColumnDescription = "更新时间")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
