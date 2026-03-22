using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.UserToolPolicy;

[ServiceDescription(typeof(IUserToolPolicyRepository), ServiceLifetime.Scoped)]
public class UserToolPolicyRepository : Repository<UserToolPolicyEntity>, IUserToolPolicyRepository
{
    public UserToolPolicyRepository(ISqlSugarClient context = null) : base(context)
    {
    }

    public async Task<List<UserToolPolicyEntity>> GetByUsernameAsync(string username)
    {
        return await GetDB().Queryable<UserToolPolicyEntity>()
            .Where(x => x.Username == username)
            .OrderBy(x => x.ToolId, OrderByType.Asc)
            .ToListAsync();
    }

    public async Task<bool> DeleteByUsernameAsync(string username)
    {
        return await DeleteAsync(x => x.Username == username);
    }
}
