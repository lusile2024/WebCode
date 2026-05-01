using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 用户上下文服务实现
/// 用于获取当前用户信息，为多用户扩展做准备
/// </summary>
[ServiceDescription(typeof(IUserContextService), ServiceLifetime.Scoped)]
public class UserContextService : IUserContextService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private string? _overrideUsername;
    
    /// <summary>
    /// 默认用户名
    /// </summary>
    private const string DefaultUsername = "default";

    public UserContextService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// 获取当前用户名
    /// 优先级：1. 已认证用户 2. 覆盖值 3. 配置文件 4. 默认值
    /// </summary>
    public string GetCurrentUsername()
    {
        var claimsUsername = _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true
            ? _httpContextAccessor.HttpContext.User.Identity?.Name
            : null;

        if (!string.IsNullOrWhiteSpace(claimsUsername))
        {
            return claimsUsername;
        }

        // 如果有覆盖值，优先于配置默认值，但不应覆盖已认证用户。
        if (!string.IsNullOrWhiteSpace(_overrideUsername))
        {
            return _overrideUsername;
        }
        
        // 从配置读取，默认为 "default"
        var configUsername = _configuration["App:DefaultUsername"];
        
        if (!string.IsNullOrWhiteSpace(configUsername))
        {
            return configUsername;
        }
        
        return DefaultUsername;
    }

    public string GetCurrentRole()
    {
        if (_httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true)
        {
            var role = _httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.Role);
            if (!string.IsNullOrWhiteSpace(role))
            {
                return role;
            }
        }

        return UserAccessConstants.UserRole;
    }

    public bool IsAuthenticated()
    {
        return !string.IsNullOrWhiteSpace(_overrideUsername) ||
               _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
    }

    /// <summary>
    /// 设置当前用户名（用于测试或特殊场景）
    /// </summary>
    public void SetCurrentUsername(string username)
    {
        _overrideUsername = username;
        // 注意：这里应该添加日志，方便调试用户上下文问题
        // Console.WriteLine($"[用户上下文] 设置当前用户名: {username}");
    }
}
