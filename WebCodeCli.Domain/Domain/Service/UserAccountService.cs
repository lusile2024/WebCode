using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.SystemSettings;
using WebCodeCli.Domain.Repositories.Base.UserAccount;

namespace WebCodeCli.Domain.Domain.Service;

[ServiceDescription(typeof(IUserAccountService), ServiceLifetime.Scoped)]
public class UserAccountService : IUserAccountService
{
    private readonly IUserAccountRepository _repository;
    private readonly ISystemSettingsRepository _systemSettingsRepository;
    private readonly AuthenticationOption _authenticationOption;
    private readonly ILogger<UserAccountService> _logger;
    private readonly PasswordHasher<UserAccountEntity> _passwordHasher = new();

    public UserAccountService(
        IUserAccountRepository repository,
        ISystemSettingsRepository systemSettingsRepository,
        IOptions<AuthenticationOption> authenticationOption,
        ILogger<UserAccountService> logger)
    {
        _repository = repository;
        _systemSettingsRepository = systemSettingsRepository;
        _authenticationOption = authenticationOption.Value;
        _logger = logger;
    }

    public async Task EnsureSeedDataAsync()
    {
        var seeds = new List<(string Username, string Password, string DisplayName, string Role)>();

        try
        {
            var adminUsername = await _systemSettingsRepository.GetAsync(SystemSettingsKeys.AdminUsername);
            var adminPassword = await _systemSettingsRepository.GetAsync(SystemSettingsKeys.AdminPassword);
            var decodedAdminPassword = DecodeLegacyPassword(adminPassword);

            if (!string.IsNullOrWhiteSpace(adminUsername) && !string.IsNullOrWhiteSpace(decodedAdminPassword))
            {
                seeds.Add((adminUsername.Trim(), decodedAdminPassword, adminUsername.Trim(), UserAccessConstants.AdminRole));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载管理员账户种子数据失败");
        }

        foreach (var user in _authenticationOption.Users)
        {
            if (string.IsNullOrWhiteSpace(user.Username) || string.IsNullOrWhiteSpace(user.Password))
            {
                continue;
            }

            var normalizedUsername = user.Username.Trim();
            var role = seeds.Any(x => string.Equals(x.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase))
                ? UserAccessConstants.AdminRole
                : UserAccessConstants.UserRole;

            seeds.Add((normalizedUsername, user.Password, normalizedUsername, role));
        }

        foreach (var seed in seeds
                     .GroupBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var existing = await _repository.GetByUsernameAsync(seed.Username);
            if (existing != null)
            {
                if (seed.Role == UserAccessConstants.AdminRole &&
                    !string.Equals(existing.Role, UserAccessConstants.AdminRole, StringComparison.OrdinalIgnoreCase))
                {
                    existing.Role = UserAccessConstants.AdminRole;
                    existing.UpdatedAt = DateTime.Now;
                    await _repository.UpdateAsync(existing);
                }

                continue;
            }

            await CreateOrUpdateAsync(new UserAccountEntity
            {
                Username = seed.Username,
                DisplayName = seed.DisplayName,
                Role = seed.Role,
                Status = UserAccessConstants.EnabledStatus
            }, seed.Password, overwritePassword: true);
        }
    }

    public async Task<List<UserAccountEntity>> GetAllAsync()
    {
        return await _repository.GetAllOrderByUsernameAsync();
    }

    public async Task<List<string>> GetAllUsernamesAsync()
    {
        var users = await _repository.GetAllOrderByUsernameAsync();
        return users
            .Where(x => !string.IsNullOrWhiteSpace(x.Username))
            .Select(x => x.Username)
            .ToList();
    }

    public async Task<UserAccountEntity?> GetByUsernameAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return await _repository.GetByUsernameAsync(username.Trim());
    }

    public async Task<UserAccountEntity?> ValidateCredentialsAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var account = await GetByUsernameAsync(username);
        if (account == null)
        {
            return null;
        }

        if (!string.Equals(account.Status, UserAccessConstants.EnabledStatus, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        return verifyResult is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded
            ? account
            : null;
    }

    public async Task<UserAccountEntity?> CreateOrUpdateAsync(UserAccountEntity account, string? plainPassword = null, bool overwritePassword = true)
    {
        if (string.IsNullOrWhiteSpace(account.Username))
        {
            return null;
        }

        var username = account.Username.Trim();
        var now = DateTime.Now;
        var existing = await _repository.GetByUsernameAsync(username);

        if (existing == null)
        {
            var entity = new UserAccountEntity
            {
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? username : account.DisplayName.Trim(),
                Role = UserAccessConstants.NormalizeRole(account.Role),
                Status = UserAccessConstants.NormalizeStatus(account.Status),
                CreatedAt = now,
                UpdatedAt = now,
                LastLoginAt = account.LastLoginAt
            };

            entity.PasswordHash = _passwordHasher.HashPassword(entity, plainPassword ?? Guid.NewGuid().ToString("N"));
            await _repository.InsertAsync(entity);
            return entity;
        }

        existing.DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? existing.DisplayName : account.DisplayName.Trim();
        existing.Role = UserAccessConstants.NormalizeRole(account.Role);
        existing.Status = UserAccessConstants.NormalizeStatus(account.Status);
        existing.LastLoginAt = account.LastLoginAt ?? existing.LastLoginAt;
        existing.UpdatedAt = now;

        if (overwritePassword && !string.IsNullOrWhiteSpace(plainPassword))
        {
            existing.PasswordHash = _passwordHasher.HashPassword(existing, plainPassword);
        }

        await _repository.UpdateAsync(existing);
        return existing;
    }

    public async Task<bool> IsEnabledAsync(string username)
    {
        var account = await GetByUsernameAsync(username);
        return account != null &&
               string.Equals(account.Status, UserAccessConstants.EnabledStatus, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsAdminAsync(string username)
    {
        var account = await GetByUsernameAsync(username);
        return account != null &&
               string.Equals(account.Role, UserAccessConstants.AdminRole, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> SetStatusAsync(string username, string status)
    {
        var account = await GetByUsernameAsync(username);
        if (account == null)
        {
            return false;
        }

        account.Status = UserAccessConstants.NormalizeStatus(status);
        account.UpdatedAt = DateTime.Now;
        return await _repository.UpdateAsync(account);
    }

    public async Task<bool> UpdateLastLoginAsync(string username, DateTime? lastLoginAt = null)
    {
        var account = await GetByUsernameAsync(username);
        if (account == null)
        {
            return false;
        }

        account.LastLoginAt = lastLoginAt ?? DateTime.Now;
        account.UpdatedAt = DateTime.Now;
        return await _repository.UpdateAsync(account);
    }

    private static string? DecodeLegacyPassword(string? encodedPassword)
    {
        if (string.IsNullOrWhiteSpace(encodedPassword))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encodedPassword));
        }
        catch
        {
            return null;
        }
    }
}
