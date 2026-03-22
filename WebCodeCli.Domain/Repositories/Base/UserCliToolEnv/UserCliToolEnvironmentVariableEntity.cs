using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.UserCliToolEnv;

[SugarTable("UserCliToolEnvironmentVariable")]
[SugarIndex("IX_UserCliToolEnv_Username_ToolId_Key", nameof(Username), OrderByType.Asc, nameof(ToolId), OrderByType.Asc, nameof(Key), OrderByType.Asc, IsUnique = true)]
public class UserCliToolEnvironmentVariableEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(Length = 128, IsNullable = false)]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(Length = 64, IsNullable = false)]
    public string ToolId { get; set; } = string.Empty;

    [SugarColumn(Length = 200, IsNullable = false)]
    public string Key { get; set; } = string.Empty;

    [SugarColumn(Length = 4000, IsNullable = true)]
    public string? Value { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
