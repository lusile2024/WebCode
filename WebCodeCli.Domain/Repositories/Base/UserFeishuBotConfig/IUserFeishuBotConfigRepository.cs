using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

public interface IUserFeishuBotConfigRepository : IRepository<UserFeishuBotConfigEntity>
{
    Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username);
}
