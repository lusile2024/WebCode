using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

/// <summary>
/// 工作区所有者仓储接口
/// </summary>
public interface IWorkspaceOwnerRepository : IRepository<WorkspaceOwnerEntity>
{
    /// <summary>
    /// 根据目录路径获取所有者信息
    /// </summary>
    /// <param name="directoryPath">规范化后的目录路径</param>
    Task<WorkspaceOwnerEntity?> GetByDirectoryPathAsync(string directoryPath);

    /// <summary>
    /// 根据用户名获取所有拥有的目录
    /// </summary>
    /// <param name="username">所有者用户名</param>
    Task<List<WorkspaceOwnerEntity>> GetByOwnerUsernameAsync(string username);

    /// <summary>
    /// 检查用户是否是目录的所有者
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="username">用户名</param>
    Task<bool> IsOwnerAsync(string directoryPath, string username);

    /// <summary>
    /// 注册目录所有者
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="username">所有者用户名</param>
    /// <param name="alias">目录别名</param>
    /// <param name="isTrusted">是否受信任</param>
    Task<WorkspaceOwnerEntity> RegisterOwnerAsync(string directoryPath, string username, string? alias = null, bool isTrusted = false);
}
