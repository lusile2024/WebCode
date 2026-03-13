using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 用户上下文服务实现
/// 用于获取当前用户信息，为多用户扩展做准备
/// </summary>
[ServiceDescription(typeof(IUserContextService), ServiceLifetime.Scoped)]
public class UserContextService : IUserContextService
{
    private readonly IConfiguration _configuration;
    private string? _overrideUsername;
    
    /// <summary>
    /// 默认用户名
    /// </summary>
    private const string DefaultUsername = "default";

    public UserContextService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// 获取当前用户名
    /// 优先级：1. 覆盖值 2. 配置文件 3. 默认值
    /// </summary>
    public string GetCurrentUsername()
    {
        // 如果有覆盖值，优先使用
        if (!string.IsNullOrWhiteSpace(_overrideUsername))
        {
            return _overrideUsername;
        }
        
        // 从配置读取，默认为 "default"
        // 未来可从 HttpContext.User 获取
        var configUsername = _configuration["App:DefaultUsername"];
        
        if (!string.IsNullOrWhiteSpace(configUsername))
        {
            return configUsername;
        }
        
        return DefaultUsername;
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
