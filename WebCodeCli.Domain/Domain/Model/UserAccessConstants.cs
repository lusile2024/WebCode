namespace WebCodeCli.Domain.Domain.Model;

public static class UserAccessConstants
{
    public const string AdminRole = "Admin";
    public const string UserRole = "User";

    public const string EnabledStatus = "Enabled";
    public const string DisabledStatus = "Disabled";

    public static string NormalizeRole(string? role)
    {
        return string.Equals(role, AdminRole, StringComparison.OrdinalIgnoreCase)
            ? AdminRole
            : UserRole;
    }

    public static string NormalizeStatus(string? status)
    {
        return string.Equals(status, DisabledStatus, StringComparison.OrdinalIgnoreCase)
            ? DisabledStatus
            : EnabledStatus;
    }
}
