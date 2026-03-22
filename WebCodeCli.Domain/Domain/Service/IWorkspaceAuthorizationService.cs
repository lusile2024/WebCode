namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 工作区授权管理服务接口
/// 管理目录的授权、权限检查、授权撤销
/// </summary>
public interface IWorkspaceAuthorizationService
{
    /// <summary>
    /// 检查用户是否有目录的访问权限
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="username">用户名</param>
    /// <param name="requiredPermission">需要的权限（read/write/admin）</param>
    Task<bool> CheckPermissionAsync(string directoryPath, string username, string requiredPermission = "read");

    /// <summary>
    /// 授予用户目录访问权限
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="ownerUsername">目录所有者用户名（验证所有权）</param>
    /// <param name="authorizedUsername">被授权用户名</param>
    /// <param name="permission">授权权限级别</param>
    /// <param name="expiresAt">过期时间（null表示永久有效）</param>
    Task<Repositories.Base.Workspace.WorkspaceAuthorizationEntity> GrantPermissionAsync(string directoryPath, string ownerUsername, string authorizedUsername, string permission = "read", DateTime? expiresAt = null);

    /// <summary>
    /// 撤销用户的目录访问权限
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="ownerUsername">目录所有者用户名（验证所有权）</param>
    /// <param name="authorizedUsername">被授权用户名</param>
    Task<bool> RevokePermissionAsync(string directoryPath, string ownerUsername, string authorizedUsername);

    /// <summary>
    /// 获取目录的所有授权用户列表
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="ownerUsername">目录所有者用户名（验证所有权）</param>
    Task<List<Repositories.Base.Workspace.WorkspaceAuthorizationEntity>> GetDirectoryAuthorizationsAsync(string directoryPath, string ownerUsername);

    /// <summary>
    /// 获取用户被授权的所有目录
    /// </summary>
    /// <param name="username">用户名</param>
    Task<List<Repositories.Base.Workspace.WorkspaceAuthorizationEntity>> GetUserAuthorizedDirectoriesAsync(string username);
}
