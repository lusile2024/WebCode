using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

[ServiceDescription(typeof(IUserFeishuBotConfigRepository), ServiceLifetime.Scoped)]
public class UserFeishuBotConfigRepository : Repository<UserFeishuBotConfigEntity>, IUserFeishuBotConfigRepository
{
    public UserFeishuBotConfigRepository(ISqlSugarClient context = null) : base(context)
    {
    }

    public async Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username)
    {
        return await GetFirstAsync(x => x.Username == username);
    }
}
