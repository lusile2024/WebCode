using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

/// <summary>
/// 工作区授权仓储接口
/// </summary>
public interface IWorkspaceAuthorizationRepository : IRepository<WorkspaceAuthorizationEntity>
{
    /// <summary>
    /// 获取目录的所有授权记录
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    Task<List<WorkspaceAuthorizationEntity>> GetByDirectoryPathAsync(string directoryPath);

    /// <summary>
    /// 获取用户被授权的所有目录
    /// </summary>
    /// <param name="username">用户名</param>
    Task<List<WorkspaceAuthorizationEntity>> GetByAuthorizedUsernameAsync(string username);

    /// <summary>
    /// 检查用户是否有目录的指定权限
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="username">用户名</param>
    /// <param name="requiredPermission">需要的权限（read/write/admin）</param>
    Task<bool> HasPermissionAsync(string directoryPath, string username, string requiredPermission);

    /// <summary>
    /// 添加目录授权
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="authorizedUsername">被授权用户名</param>
    /// <param name="permission">权限级别</param>
    /// <param name="grantedBy">授权人用户名</param>
    /// <param name="expiresAt">过期时间</param>
    Task<WorkspaceAuthorizationEntity> AddAuthorizationAsync(string directoryPath, string authorizedUsername, string permission, string grantedBy, DateTime? expiresAt = null);

    /// <summary>
    /// 取消目录授权
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="authorizedUsername">被授权用户名</param>
    Task<bool> RevokeAuthorizationAsync(string directoryPath, string authorizedUsername);
}
