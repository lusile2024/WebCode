using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Domain.Service;

public static class UserFeishuBotOptionsFactory
{
    public static FeishuOptions CreateSharedDefaults(FeishuOptions globalOptions)
    {
        return new FeishuOptions
        {
            Enabled = globalOptions.Enabled,
            AppId = string.Empty,
            AppSecret = string.Empty,
            EncryptKey = string.Empty,
            VerificationToken = string.Empty,
            StreamingThrottleMs = globalOptions.StreamingThrottleMs,
            HttpTimeoutSeconds = globalOptions.HttpTimeoutSeconds,
            DefaultCardTitle = globalOptions.DefaultCardTitle,
            ThinkingMessage = globalOptions.ThinkingMessage
        };
    }

    public static FeishuOptions? CreateEffectiveOptions(FeishuOptions sharedDefaults, UserFeishuBotConfigEntity? config)
    {
        if (!HasUsableCredentials(config))
        {
            return null;
        }

        return new FeishuOptions
        {
            Enabled = sharedDefaults.Enabled,
            AppId = Normalize(config!.AppId) ?? string.Empty,
            AppSecret = Normalize(config.AppSecret) ?? string.Empty,
            EncryptKey = Normalize(config.EncryptKey) ?? string.Empty,
            VerificationToken = Normalize(config.VerificationToken) ?? string.Empty,
            StreamingThrottleMs = config.StreamingThrottleMs ?? sharedDefaults.StreamingThrottleMs,
            HttpTimeoutSeconds = config.HttpTimeoutSeconds ?? sharedDefaults.HttpTimeoutSeconds,
            DefaultCardTitle = Normalize(config.DefaultCardTitle) ?? sharedDefaults.DefaultCardTitle,
            ThinkingMessage = Normalize(config.ThinkingMessage) ?? sharedDefaults.ThinkingMessage
        };
    }

    public static bool HasUsableCredentials(UserFeishuBotConfigEntity? config)
    {
        return config != null
            && config.IsEnabled
            && !string.IsNullOrWhiteSpace(config.AppId)
            && !string.IsNullOrWhiteSpace(config.AppSecret);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
