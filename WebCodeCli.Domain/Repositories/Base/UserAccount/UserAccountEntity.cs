using SqlSugar;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Repositories.Base.UserAccount;

[SugarTable("UserAccount")]
[SugarIndex("IX_UserAccount_Role_Status", nameof(Role), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public class UserAccountEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 128)]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? DisplayName { get; set; }

    [SugarColumn(Length = 512, IsNullable = false)]
    public string PasswordHash { get; set; } = string.Empty;

    [SugarColumn(Length = 32, IsNullable = false)]
    public string Role { get; set; } = UserAccessConstants.UserRole;

    [SugarColumn(Length = 32, IsNullable = false)]
    public string Status { get; set; } = UserAccessConstants.EnabledStatus;

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [SugarColumn(IsNullable = true)]
    public DateTime? LastLoginAt { get; set; }
}
