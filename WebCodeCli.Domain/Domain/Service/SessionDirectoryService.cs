using Microsoft.Extensions.Configuration;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.ChatSession;
using WebCodeCli.Domain.Repositories.Base.Project;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 会话目录关联服务实现
/// </summary>
[ServiceDescription(typeof(ISessionDirectoryService), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
public class SessionDirectoryService : ISessionDirectoryService
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IWorkspaceRegistryService _registryService;
    private readonly IWorkspaceAuthorizationService _authorizationService;
    private readonly IProjectRepository _projectRepository;
    private readonly bool _autoCreateMissingDirectories;
    private readonly string[] _allowedRoots;

    public SessionDirectoryService(
        IChatSessionRepository chatSessionRepository,
        IWorkspaceRegistryService registryService,
        IWorkspaceAuthorizationService authorizationService,
        IProjectRepository projectRepository,
        IConfiguration configuration)
    {
        _chatSessionRepository = chatSessionRepository;
        _registryService = registryService;
        _authorizationService = authorizationService;
        _projectRepository = projectRepository;
        _autoCreateMissingDirectories = configuration.GetValue<bool?>("Workspace:AutoCreateMissingDirectories") ?? true;
        _allowedRoots = (configuration.GetSection("Workspace:AllowedRoots").Get<string[]>() ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => _registryService.NormalizePath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 为会话设置工作区目录
    /// </summary>
    public async Task SetSessionWorkspaceAsync(string sessionId, string username, string directoryPath, bool isCustom = true)
    {
        var session = await _chatSessionRepository.GetByIdAndUsernameAsync(sessionId, username);
        if (session == null)
        {
            throw new KeyNotFoundException($"会话不存在或您没有权限访问: {sessionId}");
        }

        var normalizedPath = _registryService.NormalizePath(directoryPath);

        // 如果是自定义目录，自动注册为所有者
        if (isCustom && !string.IsNullOrEmpty(normalizedPath))
        {
            if (_registryService.IsSensitiveDirectory(normalizedPath))
            {
                throw new UnauthorizedAccessException($"禁止访问系统敏感目录: {directoryPath}");
            }

            if (_allowedRoots.Length > 0)
            {
                var isUnderAllowedRoots = _allowedRoots
                    .Any(root => IsPathWithinRoot(root, normalizedPath));
                if (!isUnderAllowedRoots)
                {
                    throw new UnauthorizedAccessException($"目录不在允许范围内: {directoryPath}");
                }
            }

            if (!Directory.Exists(normalizedPath))
            {
                if (_autoCreateMissingDirectories)
                {
                    Directory.CreateDirectory(normalizedPath);
                }
                else
                {
                    throw new DirectoryNotFoundException($"目录不存在: {directoryPath}");
                }
            }

            await _registryService.RegisterDirectoryAsync(normalizedPath, username);

            if (!await _authorizationService.CheckPermissionAsync(normalizedPath, username, "write"))
            {
                throw new UnauthorizedAccessException($"您没有权限访问目录: {directoryPath}");
            }
        }

        session.WorkspacePath = normalizedPath;
        session.IsCustomWorkspace = isCustom;
        session.UpdatedAt = DateTime.Now;

        await _chatSessionRepository.UpdateAsync(session);
    }

    /// <summary>
    /// 获取会话的工作区目录
    /// </summary>
    public async Task<string?> GetSessionWorkspaceAsync(string sessionId, string username)
    {
        var session = await _chatSessionRepository.GetByIdAndUsernameAsync(sessionId, username);
        if (session == null)
        {
            throw new KeyNotFoundException($"会话不存在或您没有权限访问: {sessionId}");
        }

        return session.WorkspacePath;
    }

    /// <summary>
    /// 切换会话的工作区目录
    /// </summary>
    public async Task SwitchSessionWorkspaceAsync(string sessionId, string username, string newDirectoryPath)
    {
        await SetSessionWorkspaceAsync(sessionId, username, newDirectoryPath, isCustom: true);
    }

    /// <summary>
    /// 验证用户是否有权限访问会话的工作区
    /// </summary>
    public async Task<bool> VerifySessionWorkspacePermissionAsync(string sessionId, string username, string requiredPermission = "write")
    {
        var session = await _chatSessionRepository.GetByIdAndUsernameAsync(sessionId, username);
        if (session == null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(session.WorkspacePath))
        {
            // 没有设置工作区，默认允许访问
            return true;
        }

        return await _authorizationService.CheckPermissionAsync(session.WorkspacePath, username, requiredPermission);
    }

    /// <summary>
    /// 获取用户有权限访问的所有目录（拥有的 + 被授权的）
    /// </summary>
    public async Task<List<object>> GetUserAccessibleDirectoriesAsync(string username)
    {
        var owned = await _registryService.GetOwnedDirectoriesAsync(username);
        var authorized = await _authorizationService.GetUserAuthorizedDirectoriesAsync(username);
        var projects = await _projectRepository.GetByUsernameOrderByUpdatedAtAsync(username);

        var result = new List<object>();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in owned)
        {
            result.Add(new
            {
                dir.Id,
                dir.DirectoryPath,
                dir.Alias,
                dir.IsTrusted,
                dir.CreatedAt,
                dir.UpdatedAt,
                Permission = "owner",
                Type = "owned",
                DirectoryType = "workspace"
            });
            addedPaths.Add(dir.DirectoryPath);
        }

        foreach (var project in projects.Where(x => !string.IsNullOrWhiteSpace(x.LocalPath) && Directory.Exists(x.LocalPath)))
        {
            if (!addedPaths.Add(project.LocalPath!))
            {
                continue;
            }

            result.Add(new
            {
                Id = project.ProjectId,
                DirectoryPath = project.LocalPath,
                Alias = project.Name,
                IsTrusted = true,
                CreatedAt = project.CreatedAt,
                UpdatedAt = project.UpdatedAt,
                Permission = "owner",
                Type = "owned",
                DirectoryType = "project"
            });
        }

        foreach (var auth in authorized)
        {
            if (!addedPaths.Add(auth.DirectoryPath))
            {
                continue;
            }

            result.Add(new
            {
                auth.Id,
                auth.DirectoryPath,
                Alias = Path.GetFileName(auth.DirectoryPath),
                auth.Permission,
                auth.GrantedBy,
                auth.GrantedAt,
                auth.ExpiresAt,
                Type = "authorized",
                DirectoryType = "workspace"
            });
        }

        return result.OrderByDescending(x => x.GetType().GetProperty("UpdatedAt")?.GetValue(x) as DateTime? ?? DateTime.MinValue).ToList<object>();
    }

    public Task<AllowedDirectoryBrowseResult> BrowseAllowedDirectoriesAsync(string? path)
    {
        var availableRoots = _allowedRoots
            .Where(Directory.Exists)
            .Select(root => new AllowedDirectoryRootItem
            {
                Name = GetDisplayName(root),
                Path = root
            })
            .ToList();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(new AllowedDirectoryBrowseResult
            {
                HasConfiguredRoots = _allowedRoots.Length > 0,
                Roots = availableRoots
            });
        }

        var normalizedPath = _registryService.NormalizePath(path);
        if (_registryService.IsSensitiveDirectory(normalizedPath))
        {
            throw new UnauthorizedAccessException($"禁止访问系统敏感目录: {path}");
        }

        var matchedRoot = _allowedRoots.FirstOrDefault(root => IsPathWithinRoot(root, normalizedPath));
        if (string.IsNullOrWhiteSpace(matchedRoot))
        {
            throw new UnauthorizedAccessException($"目录不在允许范围内: {path}");
        }

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"目录不存在: {path}");
        }

        var directories = Directory.GetDirectories(normalizedPath)
            .Select(pathValue => new DirectoryInfo(pathValue))
            .Where(info => !info.Name.StartsWith(".", StringComparison.Ordinal))
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .Select(info => new AllowedDirectoryBrowseEntry
            {
                Name = info.Name,
                Path = info.FullName,
                IsDirectory = true
            });

        var files = Directory.GetFiles(normalizedPath)
            .Select(pathValue => new FileInfo(pathValue))
            .Where(info => !info.Name.StartsWith(".", StringComparison.Ordinal))
            .Where(info => !IsReservedDeviceName(info.Name))
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateFileBrowseEntry);

        var parentPath = Directory.GetParent(normalizedPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parentPath) || !IsPathWithinRoot(matchedRoot, parentPath))
        {
            parentPath = null;
        }

        return Task.FromResult(new AllowedDirectoryBrowseResult
        {
            HasConfiguredRoots = _allowedRoots.Length > 0,
            CurrentPath = normalizedPath,
            ParentPath = parentPath,
            RootPath = matchedRoot,
            Roots = availableRoots,
            Entries = directories.Concat(files).ToList()
        });
    }

    private static bool IsPathWithinRoot(string rootPath, string targetPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedTarget.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisplayName(string path)
    {
        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }

    private static AllowedDirectoryBrowseEntry CreateFileBrowseEntry(FileInfo info)
    {
        return new AllowedDirectoryBrowseEntry
        {
            Name = info.Name,
            Path = info.FullName,
            IsDirectory = false,
            Size = GetFileSizeSafely(info),
            Extension = info.Extension
        };
    }

    private static long GetFileSizeSafely(FileInfo info)
    {
        try
        {
            return info.Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (NotSupportedException)
        {
            return 0;
        }
    }

    private static bool IsReservedDeviceName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return !string.IsNullOrWhiteSpace(baseName) && ReservedDeviceNames.Contains(baseName);
    }
}
