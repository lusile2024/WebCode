namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 工作区注册管理服务接口
/// 管理所有工作区目录的注册、查询、更新、删除
/// </summary>
public interface IWorkspaceRegistryService
{
    /// <summary>
    /// 规范化目录路径（统一处理Windows/Linux路径格式）
    /// </summary>
    /// <param name="path">原始路径</param>
    string NormalizePath(string path);

    /// <summary>
    /// 检查路径是否为敏感系统目录（禁止访问）
    /// </summary>
    /// <param name="normalizedPath">规范化后的路径</param>
    bool IsSensitiveDirectory(string normalizedPath);

    /// <summary>
    /// 注册目录所有者（如果目录未注册则自动注册）
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="username">所有者用户名</param>
    /// <param name="alias">目录别名</param>
    /// <param name="isTrusted">是否受信任</param>
    Task<Repositories.Base.Workspace.WorkspaceOwnerEntity> RegisterDirectoryAsync(string directoryPath, string username, string? alias = null, bool isTrusted = false);

    /// <summary>
    /// 获取用户拥有的所有目录
    /// </summary>
    /// <param name="username">用户名</param>
    Task<List<Repositories.Base.Workspace.WorkspaceOwnerEntity>> GetOwnedDirectoriesAsync(string username);

    /// <summary>
    /// 获取目录的所有者信息
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    Task<Repositories.Base.Workspace.WorkspaceOwnerEntity?> GetDirectoryOwnerAsync(string directoryPath);

    /// <summary>
    /// 检查用户是否是目录的所有者
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="username">用户名</param>
    Task<bool> IsDirectoryOwnerAsync(string directoryPath, string username);

    /// <summary>
    /// 更新目录信息
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="alias">新的别名</param>
    /// <param name="isTrusted">新的受信任状态</param>
    Task<bool> UpdateDirectoryInfoAsync(string directoryPath, string? alias = null, bool? isTrusted = null);

    /// <summary>
    /// 删除目录注册
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    Task<bool> DeleteDirectoryAsync(string directoryPath);
}
