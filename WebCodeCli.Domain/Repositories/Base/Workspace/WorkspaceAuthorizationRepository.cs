using AntSK.Domain.Repositories.Base;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

/// <summary>
/// 工作区授权仓储实现
/// </summary>
[ServiceDescription(typeof(IWorkspaceAuthorizationRepository), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
public class WorkspaceAuthorizationRepository : Repository<WorkspaceAuthorizationEntity>, IWorkspaceAuthorizationRepository
{
    /// <summary>
    /// 获取目录的所有授权记录
    /// </summary>
    public async Task<List<WorkspaceAuthorizationEntity>> GetByDirectoryPathAsync(string directoryPath)
    {
        return await GetListAsync(x => x.DirectoryPath == directoryPath);
    }

    /// <summary>
    /// 获取用户被授权的所有目录
    /// </summary>
    public async Task<List<WorkspaceAuthorizationEntity>> GetByAuthorizedUsernameAsync(string username)
    {
        return await GetListAsync(x => x.AuthorizedUsername == username &&
            (x.ExpiresAt == null || x.ExpiresAt > DateTime.Now));
    }

    /// <summary>
    /// 检查用户是否有目录的指定权限
    /// </summary>
    public async Task<bool> HasPermissionAsync(string directoryPath, string username, string requiredPermission)
    {
        var authorization = await GetFirstAsync(x =>
            x.DirectoryPath == directoryPath &&
            x.AuthorizedUsername == username &&
            (x.ExpiresAt == null || x.ExpiresAt > DateTime.Now));

        if (authorization == null)
        {
            return false;
        }

        // 权限级别：admin > write > read
        var permissionLevels = new Dictionary<string, int>
        {
            {"read", 1},
            {"write", 2},
            {"admin", 3}
        };

        if (!permissionLevels.TryGetValue(authorization.Permission.ToLower(), out var userLevel) ||
            !permissionLevels.TryGetValue(requiredPermission.ToLower(), out var requiredLevel))
        {
            return false;
        }

        return userLevel >= requiredLevel;
    }

    /// <summary>
    /// 添加目录授权
    /// </summary>
    public async Task<WorkspaceAuthorizationEntity> AddAuthorizationAsync(string directoryPath, string authorizedUsername, string permission, string grantedBy, DateTime? expiresAt = null)
    {
        var existing = await GetFirstAsync(x =>
            x.DirectoryPath == directoryPath &&
            x.AuthorizedUsername == authorizedUsername);

        if (existing != null)
        {
            existing.Permission = permission;
            existing.GrantedBy = grantedBy;
            existing.GrantedAt = DateTime.Now;
            existing.ExpiresAt = expiresAt;
            await UpdateAsync(existing);
            return existing;
        }

        var entity = new WorkspaceAuthorizationEntity
        {
            DirectoryPath = directoryPath,
            AuthorizedUsername = authorizedUsername,
            Permission = permission.ToLower(),
            GrantedBy = grantedBy,
            GrantedAt = DateTime.Now,
            ExpiresAt = expiresAt
        };

        await InsertAsync(entity);
        return entity;
    }

    /// <summary>
    /// 取消目录授权
    /// </summary>
    public async Task<bool> RevokeAuthorizationAsync(string directoryPath, string authorizedUsername)
    {
        return await DeleteAsync(x => x.DirectoryPath == directoryPath && x.AuthorizedUsername == authorizedUsername);
    }
}
