using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.FeishuUserBinding;
using WebCodeCli.Domain.Repositories.Base.SystemSettings;

namespace WebCodeCli.Domain.Domain.Service;

[ServiceDescription(typeof(IFeishuUserBindingService), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
public class FeishuUserBindingService : IFeishuUserBindingService
{
    private readonly IFeishuUserBindingRepository _bindingRepository;
    private readonly IUserAccountService _userAccountService;
    private readonly IUserFeishuBotConfigService _userFeishuBotConfigService;

    public FeishuUserBindingService(
        IFeishuUserBindingRepository bindingRepository,
        IUserAccountService userAccountService,
        IUserFeishuBotConfigService userFeishuBotConfigService)
    {
        _bindingRepository = bindingRepository;
        _userAccountService = userAccountService;
        _userFeishuBotConfigService = userFeishuBotConfigService;
    }

    public async Task<string?> GetBoundWebUsernameAsync(string feishuUserId)
    {
        if (string.IsNullOrWhiteSpace(feishuUserId))
        {
            return null;
        }

        var binding = await _bindingRepository.GetByFeishuUserIdAsync(feishuUserId);
        return binding?.WebUsername;
    }

    public async Task<bool> IsBoundAsync(string feishuUserId)
    {
        return !string.IsNullOrWhiteSpace(await GetBoundWebUsernameAsync(feishuUserId));
    }

    public async Task<(bool Success, string? ErrorMessage, string? WebUsername)> BindAsync(string feishuUserId, string webUsername, string? appId = null)
    {
        if (string.IsNullOrWhiteSpace(feishuUserId))
        {
            return (false, "飞书用户标识为空", null);
        }

        if (string.IsNullOrWhiteSpace(webUsername))
        {
            return (false, "请输入 Web 用户名", null);
        }

        var normalizedUsername = webUsername.Trim();
        var configuredUsername = await GetConfiguredUsernameByAppIdAsync(appId);
        if (!string.IsNullOrWhiteSpace(appId) && string.IsNullOrWhiteSpace(configuredUsername))
        {
            return (false, "当前飞书机器人未在用户管理中配置有效用户名，无法绑定。", null);
        }

        var account = await _userAccountService.GetByUsernameAsync(normalizedUsername);
        var validation = FeishuBindingUsernameValidationHelper.Validate(
            normalizedUsername,
            account?.Username,
            configuredUsername);

        if (!validation.Success)
        {
            return (false, validation.ErrorMessage, null);
        }

        var existing = await _bindingRepository.GetByFeishuUserIdAsync(feishuUserId);
        if (existing == null)
        {
            await _bindingRepository.InsertAsync(new FeishuUserBindingEntity
            {
                FeishuUserId = feishuUserId,
                WebUsername = validation.WebUsername!,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }
        else
        {
            existing.WebUsername = validation.WebUsername!;
            existing.UpdatedAt = DateTime.Now;
            await _bindingRepository.UpdateAsync(existing);
        }

        return (true, null, validation.WebUsername);
    }

    public async Task<bool> UnbindAsync(string feishuUserId)
    {
        return await _bindingRepository.DeleteAsync(x => x.FeishuUserId == feishuUserId);
    }

    public async Task<List<string>> GetBindableWebUsernamesAsync(string? appId = null)
    {
        var configuredUsername = await GetConfiguredUsernameByAppIdAsync(appId);
        if (!string.IsNullOrWhiteSpace(appId))
        {
            return string.IsNullOrWhiteSpace(configuredUsername)
                ? new List<string>()
                : new List<string> { configuredUsername };
        }

        return await _userAccountService.GetAllUsernamesAsync();
    }

    public async Task<HashSet<string>> GetAllBoundWebUsernamesAsync()
    {
        var bindings = await _bindingRepository.GetListAsync();
        return bindings
            .Where(x => !string.IsNullOrWhiteSpace(x.WebUsername))
            .Select(x => x.WebUsername)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string?> GetConfiguredUsernameByAppIdAsync(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        var config = await _userFeishuBotConfigService.GetByAppIdAsync(appId.Trim());
        var configuredUsername = config?.Username?.Trim();
        if (string.IsNullOrWhiteSpace(configuredUsername))
        {
            return null;
        }

        var account = await _userAccountService.GetByUsernameAsync(configuredUsername);
        if (account == null || !string.Equals(account.Username, configuredUsername, StringComparison.Ordinal))
        {
            return null;
        }

        return account.Username;
    }
}
