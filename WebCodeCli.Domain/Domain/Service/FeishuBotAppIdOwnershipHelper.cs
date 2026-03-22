using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Domain.Service;

public static class FeishuBotAppIdOwnershipHelper
{
    public static string? FindConflictingUsername(
        string currentUsername,
        string? appId,
        IEnumerable<UserFeishuBotConfigEntity> configs)
    {
        var normalizedCurrentUsername = Normalize(currentUsername);
        var normalizedAppId = Normalize(appId);
        if (normalizedAppId == null)
        {
            return null;
        }

        foreach (var config in configs)
        {
            var candidateUsername = Normalize(config.Username);
            var candidateAppId = Normalize(config.AppId);
            if (candidateAppId == null)
            {
                continue;
            }

            if (string.Equals(candidateUsername, normalizedCurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(candidateAppId, normalizedAppId, StringComparison.OrdinalIgnoreCase))
            {
                return candidateUsername ?? config.Username;
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
