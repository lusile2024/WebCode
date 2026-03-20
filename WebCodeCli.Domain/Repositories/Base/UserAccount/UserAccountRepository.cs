using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Repositories.Base.UserAccount;

[ServiceDescription(typeof(IUserAccountRepository), ServiceLifetime.Scoped)]
public class UserAccountRepository : Repository<UserAccountEntity>, IUserAccountRepository
{
    public UserAccountRepository(ISqlSugarClient context = null) : base(context)
    {
    }

    public async Task<UserAccountEntity?> GetByUsernameAsync(string username)
    {
        return await GetFirstAsync(x => x.Username == username);
    }

    public async Task<List<UserAccountEntity>> GetAllOrderByUsernameAsync()
    {
        return await GetDB().Queryable<UserAccountEntity>()
            .OrderBy(x => x.Username, OrderByType.Asc)
            .ToListAsync();
    }

    public async Task<List<UserAccountEntity>> GetEnabledUsersAsync()
    {
        return await GetDB().Queryable<UserAccountEntity>()
            .Where(x => x.Status == UserAccessConstants.EnabledStatus)
            .OrderBy(x => x.Username, OrderByType.Asc)
            .ToListAsync();
    }
}
