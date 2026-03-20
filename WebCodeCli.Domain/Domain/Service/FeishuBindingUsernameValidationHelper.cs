namespace WebCodeCli.Domain.Domain.Service;

public static class FeishuBindingUsernameValidationHelper
{
    public static (bool Success, string? ErrorMessage, string? WebUsername) Validate(
        string requestedUsername,
        string? actualUsername,
        string? configuredUsername)
    {
        if (string.IsNullOrWhiteSpace(actualUsername))
        {
            return (false, $"Web 用户不存在: {requestedUsername}", null);
        }

        if (!string.Equals(requestedUsername, actualUsername, StringComparison.Ordinal))
        {
            return (false, $"绑定用户名必须与用户管理中配置的用户名完全一致：{actualUsername}", null);
        }

        if (!string.IsNullOrWhiteSpace(configuredUsername)
            && !string.Equals(actualUsername, configuredUsername, StringComparison.Ordinal))
        {
            return (false, $"当前飞书机器人仅允许绑定用户：{configuredUsername}", null);
        }

        return (true, null, actualUsername);
    }
}
