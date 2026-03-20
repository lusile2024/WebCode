using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.UserAccount;

public interface IUserAccountRepository : IRepository<UserAccountEntity>
{
    Task<UserAccountEntity?> GetByUsernameAsync(string username);
    Task<List<UserAccountEntity>> GetAllOrderByUsernameAsync();
    Task<List<UserAccountEntity>> GetEnabledUsersAsync();
}
