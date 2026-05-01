using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Domain.Service;

public interface IUserFeishuBotConfigService
{
    Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username);
    Task<UserFeishuBotConfigEntity?> GetByAppIdAsync(string appId);
    Task<UserFeishuBotConfigSaveResult> SaveAsync(UserFeishuBotConfigEntity config);
    Task<bool> DeleteAsync(string username);
    Task<string?> FindConflictingUsernameByAppIdAsync(string username, string? appId);
    Task<List<UserFeishuBotConfigEntity>> GetAutoStartCandidatesAsync();
    Task<bool> UpdateRuntimePreferenceAsync(string username, bool autoStartEnabled, DateTime? lastStartedAt = null);
    FeishuOptions GetSharedDefaults();
    Task<FeishuOptions> GetEffectiveOptionsAsync(string? username);
    Task<FeishuOptions?> GetEffectiveOptionsByAppIdAsync(string? appId);
}
