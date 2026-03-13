using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

/// <summary>
/// 工作区所有者实体
/// 存储目录与所有者的关联关系
/// </summary>
[SugarTable("WorkspaceOwner")]
[SugarIndex("idx_directory_path", nameof(DirectoryPath), OrderByType.Asc, IsUnique = true)]
public class WorkspaceOwnerEntity
{
    /// <summary>
    /// 主键ID
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 目录路径（规范化后的绝对路径）
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = false)]
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>
    /// 所有者用户名
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string OwnerUsername { get; set; } = string.Empty;

    /// <summary>
    /// 目录别名（用户自定义名称）
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? Alias { get; set; }

    /// <summary>
    /// 是否为受信任目录（可执行命令）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public bool IsTrusted { get; set; } = false;

    /// <summary>
    /// 创建时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
