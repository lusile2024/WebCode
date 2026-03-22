using WebCodeCli.Domain.Repositories.Base.UserAccount;

namespace WebCodeCli.Domain.Domain.Service;

public interface IUserAccountService
{
    Task EnsureSeedDataAsync();
    Task<List<UserAccountEntity>> GetAllAsync();
    Task<List<string>> GetAllUsernamesAsync();
    Task<UserAccountEntity?> GetByUsernameAsync(string username);
    Task<UserAccountEntity?> ValidateCredentialsAsync(string username, string password);
    Task<UserAccountEntity?> CreateOrUpdateAsync(UserAccountEntity account, string? plainPassword = null, bool overwritePassword = true);
    Task<bool> IsEnabledAsync(string username);
    Task<bool> IsAdminAsync(string username);
    Task<bool> SetStatusAsync(string username, string status);
    Task<bool> UpdateLastLoginAsync(string username, DateTime? lastLoginAt = null);
}
