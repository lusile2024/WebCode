using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.Workspace;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 工作区注册管理服务实现
/// </summary>
[ServiceDescription(typeof(IWorkspaceRegistryService), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
public class WorkspaceRegistryService : IWorkspaceRegistryService
{
    private readonly IWorkspaceOwnerRepository _workspaceOwnerRepository;

    // 敏感系统目录列表（Windows + Linux）
    private static readonly HashSet<string> _sensitiveDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows系统目录
        @"C:\Windows",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\ProgramData",
        @"C:\System Volume Information",
        @"C:\Recovery",
        // Linux/Unix系统目录
        "/root",
        "/etc",
        "/bin",
        "/sbin",
        "/usr/bin",
        "/usr/sbin",
        "/boot",
        "/dev",
        "/proc",
        "/sys",
        "/var/run",
        "/var/spool/cron"
    };

    public WorkspaceRegistryService(IWorkspaceOwnerRepository workspaceOwnerRepository)
    {
        _workspaceOwnerRepository = workspaceOwnerRepository;
    }

    /// <summary>
    /// 规范化目录路径
    /// </summary>
    public string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        // 统一路径分隔符
        path = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        // 去除末尾的分隔符
        path = path.TrimEnd(Path.DirectorySeparatorChar);

        // 转为绝对路径
        if (!Path.IsPathRooted(path))
        {
            path = Path.GetFullPath(path);
        }

        return path;
    }

    /// <summary>
    /// 检查路径是否为敏感系统目录
    /// </summary>
    public bool IsSensitiveDirectory(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        var path = normalizedPath.TrimEnd(Path.DirectorySeparatorChar);

        // 检查是否直接匹配敏感目录
        if (_sensitiveDirectories.Contains(path, StringComparer.OrdinalIgnoreCase))
            return true;

        // 检查是否是敏感目录的子目录
        foreach (var sensitiveDir in _sensitiveDirectories)
        {
            var normalizedSensitiveDir = sensitiveDir.Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);

            if (path.StartsWith(normalizedSensitiveDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 注册目录所有者
    /// </summary>
    public async Task<WorkspaceOwnerEntity> RegisterDirectoryAsync(string directoryPath, string username, string? alias = null, bool isTrusted = false)
    {
        var normalizedPath = NormalizePath(directoryPath);
        // 添加日志：方便调试注册过程
        // Console.WriteLine($"[工作区注册] 注册目录: {normalizedPath}, 用户: {username}, 别名: {alias ?? Path.GetFileName(directoryPath)}");

        if (IsSensitiveDirectory(normalizedPath))
        {
            throw new UnauthorizedAccessException($"禁止访问系统敏感目录: {directoryPath}");
        }

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"目录不存在: {directoryPath}");
        }

        return await _workspaceOwnerRepository.RegisterOwnerAsync(normalizedPath, username, alias, isTrusted);
    }

    /// <summary>
    /// 获取用户拥有的所有目录
    /// </summary>
    public async Task<List<WorkspaceOwnerEntity>> GetOwnedDirectoriesAsync(string username)
    {
        return await _workspaceOwnerRepository.GetByOwnerUsernameAsync(username);
    }

    /// <summary>
    /// 获取目录的所有者信息
    /// </summary>
    public async Task<WorkspaceOwnerEntity?> GetDirectoryOwnerAsync(string directoryPath)
    {
        var normalizedPath = NormalizePath(directoryPath);
        return await _workspaceOwnerRepository.GetByDirectoryPathAsync(normalizedPath);
    }

    /// <summary>
    /// 检查用户是否是目录的所有者
    /// </summary>
    public async Task<bool> IsDirectoryOwnerAsync(string directoryPath, string username)
    {
        var normalizedPath = NormalizePath(directoryPath);
        return await _workspaceOwnerRepository.IsOwnerAsync(normalizedPath, username);
    }

    /// <summary>
    /// 更新目录信息
    /// </summary>
    public async Task<bool> UpdateDirectoryInfoAsync(string directoryPath, string? alias = null, bool? isTrusted = null)
    {
        var normalizedPath = NormalizePath(directoryPath);
        var entity = await _workspaceOwnerRepository.GetByDirectoryPathAsync(normalizedPath);

        if (entity == null)
            return false;

        if (alias != null)
            entity.Alias = alias;

        if (isTrusted.HasValue)
            entity.IsTrusted = isTrusted.Value;

        entity.UpdatedAt = DateTime.Now;
        return await _workspaceOwnerRepository.UpdateAsync(entity);
    }

    /// <summary>
    /// 删除目录注册
    /// </summary>
    public async Task<bool> DeleteDirectoryAsync(string directoryPath)
    {
        var normalizedPath = NormalizePath(directoryPath);
        return await _workspaceOwnerRepository.DeleteAsync(x => x.DirectoryPath == normalizedPath);
    }
}
