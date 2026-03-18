using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.UserCliToolEnv;

public interface IUserCliToolEnvironmentVariableRepository : IRepository<UserCliToolEnvironmentVariableEntity>
{
    Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string username, string toolId);
    Task<bool> SaveEnvironmentVariablesAsync(string username, string toolId, Dictionary<string, string> envVars);
    Task<bool> DeleteByToolIdAsync(string username, string toolId);
}
