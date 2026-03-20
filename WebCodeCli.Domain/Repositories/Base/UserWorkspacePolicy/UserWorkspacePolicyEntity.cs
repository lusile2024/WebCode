using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.UserWorkspacePolicy;

[SugarTable("UserWorkspacePolicy")]
[SugarIndex("IX_UserWorkspacePolicy_Username_DirectoryPath", nameof(Username), OrderByType.Asc, nameof(DirectoryPath), OrderByType.Asc, IsUnique = true)]
public class UserWorkspacePolicyEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(Length = 128, IsNullable = false)]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(Length = 1024, IsNullable = false)]
    public string DirectoryPath { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
