using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Domain.Service;

public interface IUserFeishuBotConfigService
{
    Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username);
    Task<bool> SaveAsync(UserFeishuBotConfigEntity config);
    Task<bool> DeleteAsync(string username);
    Task<FeishuOptions> GetEffectiveOptionsAsync(string? username);
}
