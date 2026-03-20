using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.UserToolPolicy;

[SugarTable("UserToolPolicy")]
[SugarIndex("IX_UserToolPolicy_Username_ToolId", nameof(Username), OrderByType.Asc, nameof(ToolId), OrderByType.Asc, IsUnique = true)]
public class UserToolPolicyEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(Length = 128, IsNullable = false)]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(Length = 64, IsNullable = false)]
    public string ToolId { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public bool IsAllowed { get; set; } = true;

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
