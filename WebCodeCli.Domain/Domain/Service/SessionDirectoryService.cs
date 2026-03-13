using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 会话目录关联服务实现
/// </summary>
[ServiceDescription(typeof(ISessionDirectoryService), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
public class SessionDirectoryService : ISessionDirectoryService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IWorkspaceRegistryService _registryService;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public SessionDirectoryService(
        IChatSessionRepository chatSessionRepository,
        IWorkspaceRegistryService registryService,
        IWorkspaceAuthorizationService authorizationService)
    {
        _chatSessionRepository = chatSessionRepository;
        _registryService = registryService;
        _authorizationService = authorizationService;
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
            // 检查是否是敏感目录
            if (_registryService.IsSensitiveDirectory(normalizedPath))
            {
                throw new UnauthorizedAccessException($"禁止访问系统敏感目录: {directoryPath}");
            }

            // 自动注册目录所有者
            await _registryService.RegisterDirectoryAsync(normalizedPath, username);

            // 验证权限
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

        var result = new List<object>();

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
                Type = "owned"
            });
        }

        foreach (var auth in authorized)
        {
            result.Add(new
            {
                auth.Id,
                auth.DirectoryPath,
                Alias = Path.GetFileName(auth.DirectoryPath),
                auth.Permission,
                auth.GrantedBy,
                auth.GrantedAt,
                auth.ExpiresAt,
                Type = "authorized"
            });
        }

        return result;
    }
}
