using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Domain.Service;

[ServiceDescription(typeof(IUserFeishuBotConfigService), ServiceLifetime.Scoped)]
public class UserFeishuBotConfigService : IUserFeishuBotConfigService
{
    private readonly IUserFeishuBotConfigRepository _repository;
    private readonly FeishuOptions _globalOptions;

    public UserFeishuBotConfigService(
        IUserFeishuBotConfigRepository repository,
        IOptions<FeishuOptions> globalOptions)
    {
        _repository = repository;
        _globalOptions = globalOptions.Value;
    }

    public async Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return await _repository.GetByUsernameAsync(username.Trim());
    }

    public async Task<bool> SaveAsync(UserFeishuBotConfigEntity config)
    {
        if (string.IsNullOrWhiteSpace(config.Username))
        {
            return false;
        }

        var normalizedUsername = config.Username.Trim();
        var existing = await _repository.GetByUsernameAsync(normalizedUsername);
        var now = DateTime.Now;

        if (existing == null)
        {
            config.Username = normalizedUsername;
            config.CreatedAt = now;
            config.UpdatedAt = now;
            return await _repository.InsertAsync(config);
        }

        existing.IsEnabled = config.IsEnabled;
        existing.AppId = config.AppId;
        existing.AppSecret = config.AppSecret;
        existing.EncryptKey = config.EncryptKey;
        existing.VerificationToken = config.VerificationToken;
        existing.DefaultCardTitle = config.DefaultCardTitle;
        existing.ThinkingMessage = config.ThinkingMessage;
        existing.HttpTimeoutSeconds = config.HttpTimeoutSeconds;
        existing.StreamingThrottleMs = config.StreamingThrottleMs;
        existing.UpdatedAt = now;

        return await _repository.UpdateAsync(existing);
    }

    public async Task<bool> DeleteAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        return await _repository.DeleteAsync(x => x.Username == username.Trim());
    }

    public async Task<FeishuOptions> GetEffectiveOptionsAsync(string? username)
    {
        var effective = CloneGlobalOptions();
        if (string.IsNullOrWhiteSpace(username))
        {
            return effective;
        }

        var config = await _repository.GetByUsernameAsync(username.Trim());
        if (config == null || !config.IsEnabled || string.IsNullOrWhiteSpace(config.AppId) || string.IsNullOrWhiteSpace(config.AppSecret))
        {
            return effective;
        }

        effective.AppId = config.AppId;
        effective.AppSecret = config.AppSecret;
        effective.EncryptKey = config.EncryptKey ?? effective.EncryptKey;
        effective.VerificationToken = config.VerificationToken ?? effective.VerificationToken;
        effective.DefaultCardTitle = string.IsNullOrWhiteSpace(config.DefaultCardTitle) ? effective.DefaultCardTitle : config.DefaultCardTitle;
        effective.ThinkingMessage = string.IsNullOrWhiteSpace(config.ThinkingMessage) ? effective.ThinkingMessage : config.ThinkingMessage;
        effective.HttpTimeoutSeconds = config.HttpTimeoutSeconds ?? effective.HttpTimeoutSeconds;
        effective.StreamingThrottleMs = config.StreamingThrottleMs ?? effective.StreamingThrottleMs;

        return effective;
    }

    private FeishuOptions CloneGlobalOptions()
    {
        return new FeishuOptions
        {
            Enabled = _globalOptions.Enabled,
            AppId = _globalOptions.AppId,
            AppSecret = _globalOptions.AppSecret,
            EncryptKey = _globalOptions.EncryptKey,
            VerificationToken = _globalOptions.VerificationToken,
            StreamingThrottleMs = _globalOptions.StreamingThrottleMs,
            HttpTimeoutSeconds = _globalOptions.HttpTimeoutSeconds,
            DefaultCardTitle = _globalOptions.DefaultCardTitle,
            ThinkingMessage = _globalOptions.ThinkingMessage
        };
    }
}
