using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

/// <summary>
/// 工作区授权实体
/// 存储目录授权关系，授权其他用户访问目录
/// </summary>
[SugarTable("WorkspaceAuthorization")]
[SugarIndex("idx_directory_authorized_user", nameof(DirectoryPath), OrderByType.Asc, nameof(AuthorizedUsername), OrderByType.Asc, IsUnique = true)]
public class WorkspaceAuthorizationEntity
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
    /// 被授权的用户名
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string AuthorizedUsername { get; set; } = string.Empty;

    /// <summary>
    /// 授权权限级别：read（只读）、write（读写）、admin（管理）
    /// </summary>
    [SugarColumn(Length = 16, IsNullable = false)]
    public string Permission { get; set; } = "read";

    /// <summary>
    /// 授权人用户名
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string GrantedBy { get; set; } = string.Empty;

    /// <summary>
    /// 授权时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime GrantedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 过期时间（null表示永久有效）
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? ExpiresAt { get; set; }
}
