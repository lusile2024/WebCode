namespace WebCodeCli.Domain.Domain.Service;

public sealed class UserFeishuBotConfigSaveResult
{
    private UserFeishuBotConfigSaveResult(bool success, string? errorMessage, string? conflictingUsername)
    {
        Success = success;
        ErrorMessage = errorMessage;
        ConflictingUsername = conflictingUsername;
    }

    public bool Success { get; }
    public string? ErrorMessage { get; }
    public string? ConflictingUsername { get; }

    public static UserFeishuBotConfigSaveResult Saved() => new(true, null, null);

    public static UserFeishuBotConfigSaveResult Failure(string errorMessage) => new(false, errorMessage, null);

    public static UserFeishuBotConfigSaveResult Conflict(string conflictingUsername, string? appId)
    {
        var normalizedAppId = string.IsNullOrWhiteSpace(appId) ? "当前 AppId" : appId.Trim();
        return new(
            false,
            $"{normalizedAppId} 已被用户 {conflictingUsername} 使用，一个 AppId 只能绑定一个用户。",
            conflictingUsername);
    }
}
