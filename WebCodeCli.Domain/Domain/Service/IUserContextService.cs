namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 用户上下文服务接口
/// 用于获取当前用户信息，为多用户扩展做准备
/// </summary>
public interface IUserContextService
{
    /// <summary>
    /// 获取当前用户名
    /// 当前阶段返回默认值，未来可对接认证系统
    /// </summary>
    string GetCurrentUsername();

    /// <summary>
    /// 获取当前用户角色
    /// </summary>
    string GetCurrentRole();

    /// <summary>
    /// 当前请求是否已认证
    /// </summary>
    bool IsAuthenticated();
    
    /// <summary>
    /// 设置当前用户名（用于测试或特殊场景）
    /// </summary>
    void SetCurrentUsername(string username);
}
