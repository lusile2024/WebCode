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

    public FeishuUserBindingService(
        IFeishuUserBindingRepository bindingRepository,
        IUserAccountService userAccountService)
    {
        _bindingRepository = bindingRepository;
        _userAccountService = userAccountService;
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

    public async Task<(bool Success, string? ErrorMessage, string? WebUsername)> BindAsync(string feishuUserId, string webUsername)
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
        var bindableUsernames = await GetBindableWebUsernamesAsync();
        if (!bindableUsernames.Any(x => string.Equals(x, normalizedUsername, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"Web 用户不存在: {normalizedUsername}", null);
        }

        var existing = await _bindingRepository.GetByFeishuUserIdAsync(feishuUserId);
        if (existing == null)
        {
            await _bindingRepository.InsertAsync(new FeishuUserBindingEntity
            {
                FeishuUserId = feishuUserId,
                WebUsername = normalizedUsername,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }
        else
        {
            existing.WebUsername = normalizedUsername;
            existing.UpdatedAt = DateTime.Now;
            await _bindingRepository.UpdateAsync(existing);
        }

        return (true, null, normalizedUsername);
    }

    public async Task<bool> UnbindAsync(string feishuUserId)
    {
        return await _bindingRepository.DeleteAsync(x => x.FeishuUserId == feishuUserId);
    }

    public async Task<List<string>> GetBindableWebUsernamesAsync()
    {
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
}
