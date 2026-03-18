namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 会话目录关联服务接口
/// 管理会话与目录的关联关系、切换、查询
/// </summary>
public interface ISessionDirectoryService
{
    /// <summary>
    /// 为会话设置工作区目录
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="username">当前操作用户名</param>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="isCustom">是否为自定义目录</param>
    Task SetSessionWorkspaceAsync(string sessionId, string username, string directoryPath, bool isCustom = true);

    /// <summary>
    /// 获取会话的工作区目录
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="username">当前操作用户名（验证权限）</param>
    Task<string?> GetSessionWorkspaceAsync(string sessionId, string username);

    /// <summary>
    /// 切换会话的工作区目录
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="username">当前操作用户名</param>
    /// <param name="newDirectoryPath">新的目录路径</param>
    Task SwitchSessionWorkspaceAsync(string sessionId, string username, string newDirectoryPath);

    /// <summary>
    /// 验证用户是否有权限访问会话的工作区
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="username">用户名</param>
    /// <param name="requiredPermission">需要的权限</param>
    Task<bool> VerifySessionWorkspacePermissionAsync(string sessionId, string username, string requiredPermission = "write");

    /// <summary>
    /// 获取用户有权限访问的所有目录（拥有的 + 被授权的）
    /// </summary>
    /// <param name="username">用户名</param>
    Task<List<object>> GetUserAccessibleDirectoriesAsync(string username);

    /// <summary>
    /// 浏览白名单目录。
    /// 未指定 path 时返回白名单根目录列表；指定 path 时返回该目录下的目录和文件条目。
    /// </summary>
    /// <param name="path">要浏览的绝对路径，可为空</param>
    Task<AllowedDirectoryBrowseResult> BrowseAllowedDirectoriesAsync(string? path);
}

public sealed class AllowedDirectoryBrowseResult
{
    public bool HasConfiguredRoots { get; init; }
    public string? CurrentPath { get; init; }
    public string? ParentPath { get; init; }
    public string? RootPath { get; init; }
    public List<AllowedDirectoryRootItem> Roots { get; init; } = [];
    public List<AllowedDirectoryBrowseEntry> Entries { get; init; } = [];
}

public sealed class AllowedDirectoryRootItem
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

public sealed class AllowedDirectoryBrowseEntry
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public string Extension { get; init; } = string.Empty;
}
