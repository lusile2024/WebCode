using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.UserWorkspacePolicy;

[ServiceDescription(typeof(IUserWorkspacePolicyRepository), ServiceLifetime.Scoped)]
public class UserWorkspacePolicyRepository : Repository<UserWorkspacePolicyEntity>, IUserWorkspacePolicyRepository
{
    public UserWorkspacePolicyRepository(ISqlSugarClient context = null) : base(context)
    {
    }

    public async Task<List<UserWorkspacePolicyEntity>> GetByUsernameAsync(string username)
    {
        return await GetDB().Queryable<UserWorkspacePolicyEntity>()
            .Where(x => x.Username == username)
            .OrderBy(x => x.DirectoryPath, OrderByType.Asc)
            .ToListAsync();
    }

    public async Task<bool> DeleteByUsernameAsync(string username)
    {
        return await DeleteAsync(x => x.Username == username);
    }
}
