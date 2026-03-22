using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.SystemSettings;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 认证服务实现
/// </summary>
[ServiceDescription(typeof(IAuthenticationService), ServiceLifetime.Scoped)]
public class AuthenticationService : IAuthenticationService
{
    private readonly AuthenticationOption _authOption;
    private readonly ISystemSettingsRepository _settingsRepository;
    private readonly IUserAccountService _userAccountService;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IOptions<AuthenticationOption> authOption,
        ISystemSettingsRepository settingsRepository,
        IUserAccountService userAccountService,
        ILogger<AuthenticationService> logger)
    {
        _authOption = authOption.Value;
        _settingsRepository = settingsRepository;
        _userAccountService = userAccountService;
        _logger = logger;
    }

    public bool ValidateUser(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        try
        {
            var account = _userAccountService.ValidateCredentialsAsync(username, password).GetAwaiter().GetResult();
            if (account != null)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从用户账户服务验证用户失败，降级到配置文件验证");
        }

        // 降级到配置文件验证
        var user = _authOption.Users?.FirstOrDefault(u => 
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user == null)
        {
            return false;
        }

        return user.Password == password;
    }

    public bool IsAuthenticationEnabled()
    {
        try
        {
            // 优先读取数据库配置
            var dbEnabled = _settingsRepository.GetBoolAsync(SystemSettingsKeys.AuthEnabled).GetAwaiter().GetResult();
            
            // 检查是否已完成初始化（如果已初始化则使用数据库值）
            var isInitialized = _settingsRepository.IsSystemInitializedAsync().GetAwaiter().GetResult();
            if (isInitialized)
            {
                return dbEnabled;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从数据库读取认证配置失败，使用配置文件值");
        }
        
        return _authOption.Enabled;
    }
}

