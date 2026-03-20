using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.UserToolPolicy;

public interface IUserToolPolicyRepository : IRepository<UserToolPolicyEntity>
{
    Task<List<UserToolPolicyEntity>> GetByUsernameAsync(string username);
    Task<bool> DeleteByUsernameAsync(string username);
}
