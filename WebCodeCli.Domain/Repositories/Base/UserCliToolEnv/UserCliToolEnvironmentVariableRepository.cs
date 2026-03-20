using AntSK.Domain.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.UserCliToolEnv;

[ServiceDescription(typeof(IUserCliToolEnvironmentVariableRepository), ServiceLifetime.Scoped)]
public class UserCliToolEnvironmentVariableRepository : Repository<UserCliToolEnvironmentVariableEntity>, IUserCliToolEnvironmentVariableRepository
{
    public UserCliToolEnvironmentVariableRepository(ISqlSugarClient context = null) : base(context)
    {
    }

    public async Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string username, string toolId)
    {
        var list = await GetDB().Queryable<UserCliToolEnvironmentVariableEntity>()
            .Where(x => x.Username == username && x.ToolId == toolId)
            .OrderBy(x => x.Key, OrderByType.Asc)
            .ToListAsync();

        return list.ToDictionary(x => x.Key, x => x.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> SaveEnvironmentVariablesAsync(string username, string toolId, Dictionary<string, string> envVars)
    {
        await DeleteByToolIdAsync(username, toolId);
        if (!envVars.Any())
        {
            return true;
        }

        var now = DateTime.Now;
        var entities = envVars.Select(kvp => new UserCliToolEnvironmentVariableEntity
        {
            Username = username,
            ToolId = toolId,
            Key = kvp.Key,
            Value = kvp.Value,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        return await InsertRangeAsync(entities);
    }

    public async Task<bool> DeleteByToolIdAsync(string username, string toolId)
    {
        return await DeleteAsync(x => x.Username == username && x.ToolId == toolId);
    }
}
