using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.Workspace;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 工作区授权管理服务实现
/// </summary>
[ServiceDescription(typeof(IWorkspaceAuthorizationService), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
public class WorkspaceAuthorizationService : IWorkspaceAuthorizationService
{
    private readonly IWorkspaceAuthorizationRepository _authorizationRepository;
    private readonly IWorkspaceRegistryService _registryService;

    public WorkspaceAuthorizationService(
        IWorkspaceAuthorizationRepository authorizationRepository,
        IWorkspaceRegistryService registryService)
    {
        _authorizationRepository = authorizationRepository;
        _registryService = registryService;
    }

    /// <summary>
    /// 检查用户是否有目录的访问权限
    /// </summary>
    public async Task<bool> CheckPermissionAsync(string directoryPath, string username, string requiredPermission = "read")
    {
        var normalizedPath = _registryService.NormalizePath(directoryPath);

        // 所有者拥有所有权限
        if (await _registryService.IsDirectoryOwnerAsync(normalizedPath, username))
        {
            return true;
        }

        // 检查授权
        return await _authorizationRepository.HasPermissionAsync(normalizedPath, username, requiredPermission);
    }

    /// <summary>
    /// 授予用户目录访问权限
    /// </summary>
    public async Task<WorkspaceAuthorizationEntity> GrantPermissionAsync(
        string directoryPath,
        string ownerUsername,
        string authorizedUsername,
        string permission = "read",
        DateTime? expiresAt = null)
    {
        var normalizedPath = _registryService.NormalizePath(directoryPath);

        // 验证调用者是否是目录所有者
        if (!await _registryService.IsDirectoryOwnerAsync(normalizedPath, ownerUsername))
        {
            throw new UnauthorizedAccessException($"您不是目录 {directoryPath} 的所有者，无法进行授权操作");
        }

        // 不能授权给自己
        if (string.Equals(ownerUsername, authorizedUsername, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("不能对自己进行授权操作");
        }

        // 验证权限级别有效性
        var validPermissions = new[] { "read", "write", "admin" };
        if (!validPermissions.Contains(permission.ToLower()))
        {
            throw new ArgumentException($"无效的权限级别: {permission}，有效值为: read, write, admin");
        }

        return await _authorizationRepository.AddAuthorizationAsync(
            normalizedPath,
            authorizedUsername,
            permission,
            ownerUsername,
            expiresAt);
    }

    /// <summary>
    /// 撤销用户的目录访问权限
    /// </summary>
    public async Task<bool> RevokePermissionAsync(string directoryPath, string ownerUsername, string authorizedUsername)
    {
        var normalizedPath = _registryService.NormalizePath(directoryPath);

        // 验证调用者是否是目录所有者
        if (!await _registryService.IsDirectoryOwnerAsync(normalizedPath, ownerUsername))
        {
            throw new UnauthorizedAccessException($"您不是目录 {directoryPath} 的所有者，无法撤销授权");
        }

        return await _authorizationRepository.RevokeAuthorizationAsync(normalizedPath, authorizedUsername);
    }

    /// <summary>
    /// 获取目录的所有授权用户列表
    /// </summary>
    public async Task<List<WorkspaceAuthorizationEntity>> GetDirectoryAuthorizationsAsync(string directoryPath, string ownerUsername)
    {
        var normalizedPath = _registryService.NormalizePath(directoryPath);

        // 验证调用者是否是目录所有者
        if (!await _registryService.IsDirectoryOwnerAsync(normalizedPath, ownerUsername))
        {
            throw new UnauthorizedAccessException($"您不是目录 {directoryPath} 的所有者，无法查看授权列表");
        }

        return await _authorizationRepository.GetByDirectoryPathAsync(normalizedPath);
    }

    /// <summary>
    /// 获取用户被授权的所有目录
    /// </summary>
    public async Task<List<WorkspaceAuthorizationEntity>> GetUserAuthorizedDirectoriesAsync(string username)
    {
        return await _authorizationRepository.GetByAuthorizedUsernameAsync(username);
    }
}
