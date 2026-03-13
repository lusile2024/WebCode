using AntSK.Domain.Repositories.Base;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

/// <summary>
/// 工作区所有者仓储实现
/// </summary>
[ServiceDescription(typeof(IWorkspaceOwnerRepository), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
public class WorkspaceOwnerRepository : Repository<WorkspaceOwnerEntity>, IWorkspaceOwnerRepository
{
    /// <summary>
    /// 根据目录路径获取所有者信息
    /// </summary>
    public async Task<WorkspaceOwnerEntity?> GetByDirectoryPathAsync(string directoryPath)
    {
        return await GetFirstAsync(x => x.DirectoryPath == directoryPath);
    }

    /// <summary>
    /// 根据用户名获取所有拥有的目录
    /// </summary>
    public async Task<List<WorkspaceOwnerEntity>> GetByOwnerUsernameAsync(string username)
    {
        return await GetListAsync(x => x.OwnerUsername == username);
    }

    /// <summary>
    /// 检查用户是否是目录的所有者
    /// </summary>
    public async Task<bool> IsOwnerAsync(string directoryPath, string username)
    {
        return await IsAnyAsync(x => x.DirectoryPath == directoryPath && x.OwnerUsername == username);
    }

    /// <summary>
    /// 注册目录所有者
    /// </summary>
    public async Task<WorkspaceOwnerEntity> RegisterOwnerAsync(string directoryPath, string username, string? alias = null, bool isTrusted = false)
    {
        var existing = await GetByDirectoryPathAsync(directoryPath);
        if (existing != null)
        {
            return existing;
        }

        var entity = new WorkspaceOwnerEntity
        {
            DirectoryPath = directoryPath,
            OwnerUsername = username,
            Alias = alias ?? Path.GetFileName(directoryPath),
            IsTrusted = isTrusted,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        await InsertAsync(entity);
        return entity;
    }
}
