using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.UserWorkspacePolicy;

public interface IUserWorkspacePolicyRepository : IRepository<UserWorkspacePolicyEntity>
{
    Task<List<UserWorkspacePolicyEntity>> GetByUsernameAsync(string username);
    Task<bool> DeleteByUsernameAsync(string username);
}
