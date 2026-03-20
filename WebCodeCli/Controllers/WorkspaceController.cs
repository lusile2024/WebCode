using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System.Security.Claims;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Controllers;

/// <summary>
/// 工作区控制器
/// 提供静态文件访问和工作区授权管理功能
/// </summary>
[ApiController]
[Route("api/workspace")]
[Authorize]
public class WorkspaceController : ControllerBase
{
    private readonly ICliExecutorService _cliExecutorService;
    private readonly ILogger<WorkspaceController> _logger;
    private readonly FileExtensionContentTypeProvider _contentTypeProvider;
    private readonly IWorkspaceRegistryService _workspaceRegistryService;
    private readonly IWorkspaceAuthorizationService _workspaceAuthorizationService;
    private readonly ISessionDirectoryService _sessionDirectoryService;
    private readonly IUserContextService _userContextService;

    public WorkspaceController(
        ICliExecutorService cliExecutorService,
        ILogger<WorkspaceController> logger,
        IWorkspaceRegistryService workspaceRegistryService,
        IWorkspaceAuthorizationService workspaceAuthorizationService,
        ISessionDirectoryService sessionDirectoryService,
        IUserContextService userContextService)
    {
        _cliExecutorService = cliExecutorService;
        _logger = logger;
        _contentTypeProvider = new FileExtensionContentTypeProvider();
        _workspaceRegistryService = workspaceRegistryService;
        _workspaceAuthorizationService = workspaceAuthorizationService;
        _sessionDirectoryService = sessionDirectoryService;
        _userContextService = userContextService;
    }

    /// <summary>
    /// 获取当前用户名
    /// </summary>
    private string GetCurrentUsername()
    {
        // 优先使用 UserContextService，该服务在 Blazor 登录时设置用户名
        // 如果 UserContextService 返回配置默认值（未登录时），则回退到 Claims
        var username = _userContextService.GetCurrentUsername();
        _logger.LogDebug("[工作区] 获取当前用户名: {Username}", username);
        return username;
    }

    /// <summary>
    /// 获取工作区文件
    /// GET /api/workspace/{sessionId}/files/{**filePath}
    /// 例如: /api/workspace/abc123/files/index.html
    ///      /api/workspace/abc123/files/css/style.css
    /// </summary>
    [HttpGet("{sessionId}/files/{**filePath}")]
    public IActionResult GetFile(string sessionId, string filePath)
    {
        try
        {
            // 获取工作区路径
            var workspacePath = _cliExecutorService.GetSessionWorkspacePath(sessionId);

            if (!Directory.Exists(workspacePath))
            {
                _logger.LogWarning("工作区不存在: SessionId={SessionId}, Path={Path}", sessionId, workspacePath);
                return NotFound(new { error = "工作区不存在" });
            }

            // 解码文件路径（处理URL编码的中文等字符）
            filePath = Uri.UnescapeDataString(filePath);

            // 组合完整路径
            var fullPath = Path.Combine(workspacePath, filePath);

            // 安全检查：确保文件在工作区内（防止目录遍历攻击）
            var normalizedWorkspace = Path.GetFullPath(workspacePath);
            var normalizedFile = Path.GetFullPath(fullPath);

            if (!normalizedFile.StartsWith(normalizedWorkspace, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("检测到路径遍历攻击尝试: SessionId={SessionId}, FilePath={FilePath}", 
                    sessionId, filePath);
                return BadRequest(new { error = "无效的文件路径" });
            }

            // 检查文件是否存在
            if (!System.IO.File.Exists(fullPath))
            {
                _logger.LogDebug("文件不存在: {Path}", fullPath);
                return NotFound(new { error = "文件不存在", path = filePath });
            }

            // 读取文件
            var fileBytes = System.IO.File.ReadAllBytes(fullPath);

            // 确定 Content-Type
            var contentType = "application/octet-stream";
            if (_contentTypeProvider.TryGetContentType(fullPath, out var detectedContentType))
            {
                contentType = detectedContentType;
            }

            _logger.LogDebug("返回文件: {Path}, Size={Size}, ContentType={ContentType}", 
                filePath, fileBytes.Length, contentType);

            // 设置缓存头（可选，提升性能）
            Response.Headers.CacheControl = "public, max-age=300"; // 缓存5分钟

            return File(fileBytes, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取工作区文件失败: SessionId={SessionId}, FilePath={FilePath}", 
                sessionId, filePath);
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取构建输出文件（支持SPA路由fallback）
    /// GET /api/workspace/{sessionId}/build/{projectPath}/{**filePath}
    /// 例如: /api/workspace/abc123/build/my-app/assets/index.js
    /// </summary>
    [HttpGet("{sessionId}/build/{projectPath}/{**filePath}")]
    public IActionResult GetBuildFile(string sessionId, string projectPath, string? filePath = "")
    {
        try
        {
            // 获取工作区路径
            var workspacePath = _cliExecutorService.GetSessionWorkspacePath(sessionId);

            if (!Directory.Exists(workspacePath))
            {
                _logger.LogWarning("工作区不存在: SessionId={SessionId}, Path={Path}", sessionId, workspacePath);
                return NotFound(new { error = "工作区不存在" });
            }

            // 解码路径
            projectPath = Uri.UnescapeDataString(projectPath);
            filePath = string.IsNullOrEmpty(filePath) ? "index.html" : Uri.UnescapeDataString(filePath);

            // 尝试常见的构建输出目录
            var buildDirs = new[] { "dist", "build", ".next/static", ".output/public", "out" };
            string? fullPath = null;

            foreach (var buildDir in buildDirs)
            {
                var tryPath = Path.Combine(workspacePath, projectPath, buildDir, filePath);
                if (System.IO.File.Exists(tryPath))
                {
                    fullPath = tryPath;
                    break;
                }
            }

            // 如果文件不存在，尝试返回 index.html（SPA fallback）
            if (fullPath == null)
            {
                // 查找 index.html
                foreach (var buildDir in buildDirs)
                {
                    var indexPath = Path.Combine(workspacePath, projectPath, buildDir, "index.html");
                    if (System.IO.File.Exists(indexPath))
                    {
                        fullPath = indexPath;
                        _logger.LogDebug("SPA fallback: 返回 index.html for {FilePath}", filePath);
                        break;
                    }
                }
            }

            if (fullPath == null)
            {
                _logger.LogDebug("构建文件不存在: {ProjectPath}/{FilePath}", projectPath, filePath);
                return NotFound(new { error = "文件不存在", path = filePath });
            }

            // 安全检查
            var normalizedWorkspace = Path.GetFullPath(workspacePath);
            var normalizedFile = Path.GetFullPath(fullPath);

            if (!normalizedFile.StartsWith(normalizedWorkspace, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("检测到路径遍历攻击尝试: SessionId={SessionId}, FilePath={FilePath}",
                    sessionId, filePath);
                return BadRequest(new { error = "无效的文件路径" });
            }

            // 读取文件
            var fileBytes = System.IO.File.ReadAllBytes(fullPath);

            // 确定 Content-Type
            var contentType = "application/octet-stream";
            if (_contentTypeProvider.TryGetContentType(fullPath, out var detectedContentType))
            {
                contentType = detectedContentType;
            }

            _logger.LogDebug("返回构建文件: {Path}, Size={Size}, ContentType={ContentType}",
                filePath, fileBytes.Length, contentType);

            // 禁用缓存以便开发时实时更新
            Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";

            return File(fileBytes, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取构建文件失败: SessionId={SessionId}, ProjectPath={ProjectPath}, FilePath={FilePath}",
                sessionId, projectPath, filePath);
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 健康检查端点
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    #region 工作区授权管理API

    /// <summary>
    /// 授权用户访问目录
    /// </summary>
    /// <param name="request">授权请求参数</param>
    [HttpPost("authorize")]
    public async Task<IActionResult> AuthorizeDirectory([FromBody] AuthorizeDirectoryRequest request)
    {
        try
        {
            var currentUser = GetCurrentUsername();
            var authorization = await _workspaceAuthorizationService.GrantPermissionAsync(
                request.DirectoryPath,
                currentUser,
                request.AuthorizedUsername,
                request.Permission,
                request.ExpiresAt);

            return Ok(new
            {
                success = true,
                data = new
                {
                    authorization.Id,
                    authorization.DirectoryPath,
                    authorization.AuthorizedUsername,
                    authorization.Permission,
                    authorization.GrantedBy,
                    authorization.GrantedAt,
                    authorization.ExpiresAt
                }
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "目录授权失败");
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 取消用户的目录访问授权
    /// </summary>
    /// <param name="request">取消授权请求参数</param>
    [HttpPost("revoke-authorization")]
    public async Task<IActionResult> RevokeAuthorization([FromBody] RevokeAuthorizationRequest request)
    {
        try
        {
            var currentUser = GetCurrentUsername();
            var success = await _workspaceAuthorizationService.RevokePermissionAsync(
                request.DirectoryPath,
                currentUser,
                request.AuthorizedUsername);

            return Ok(new { success });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消授权失败");
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取我拥有的所有目录
    /// </summary>
    [HttpGet("my-owned-directories")]
    public async Task<IActionResult> GetMyOwnedDirectories()
    {
        try
        {
            var currentUser = GetCurrentUsername();
            var directories = await _workspaceRegistryService.GetOwnedDirectoriesAsync(currentUser);

            return Ok(new
            {
                success = true,
                data = directories.Select(d => new
                {
                    d.Id,
                    d.DirectoryPath,
                    d.Alias,
                    d.IsTrusted,
                    d.CreatedAt,
                    d.UpdatedAt
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取拥有的目录失败");
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取我被授权访问的所有目录
    /// </summary>
    [HttpGet("my-authorized-directories")]
    public async Task<IActionResult> GetMyAuthorizedDirectories()
    {
        try
        {
            var currentUser = GetCurrentUsername();
            var authorizations = await _workspaceAuthorizationService.GetUserAuthorizedDirectoriesAsync(currentUser);

            return Ok(new
            {
                success = true,
                data = authorizations.Select(a => new
                {
                    a.Id,
                    a.DirectoryPath,
                    a.Permission,
                    a.GrantedBy,
                    a.GrantedAt,
                    a.ExpiresAt
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取被授权的目录失败");
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取目录的授权用户列表
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    [HttpGet("directory-authorizations")]
    public async Task<IActionResult> GetDirectoryAuthorizations([FromQuery] string directoryPath)
    {
        try
        {
            var currentUser = GetCurrentUsername();
            var authorizations = await _workspaceAuthorizationService.GetDirectoryAuthorizationsAsync(
                directoryPath, currentUser);

            return Ok(new
            {
                success = true,
                data = authorizations.Select(a => new
                {
                    a.Id,
                    a.AuthorizedUsername,
                    a.Permission,
                    a.GrantedBy,
                    a.GrantedAt,
                    a.ExpiresAt
                })
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取目录授权列表失败");
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取我有权限访问的所有目录（拥有的 + 被授权的）
    /// </summary>
    [HttpGet("my-accessible-directories")]
    public async Task<IActionResult> GetMyAccessibleDirectories()
    {
        try
        {
            var currentUser = GetCurrentUsername();
            var directories = await _sessionDirectoryService.GetUserAccessibleDirectoriesAsync(currentUser);

            return Ok(new
            {
                success = true,
                data = directories
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取可访问目录失败");
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 浏览白名单目录。
    /// 未传 path 时返回白名单根目录；传入 path 时返回该目录下的目录和文件。
    /// </summary>
    [HttpGet("allowed-directories/browse")]
    public async Task<IActionResult> BrowseAllowedDirectories([FromQuery] string? path = null)
    {
        try
        {
            var result = await _sessionDirectoryService.BrowseAllowedDirectoriesAsync(path, GetCurrentUsername());
            return Ok(new
            {
                success = true,
                data = result
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (DirectoryNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "浏览白名单目录失败: Path={Path}", path);
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    #endregion
}

#region 请求参数模型

/// <summary>
/// 目录授权请求参数
/// </summary>
public class AuthorizeDirectoryRequest
{
    /// <summary>
    /// 目录路径
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>
    /// 被授权的用户名
    /// </summary>
    public string AuthorizedUsername { get; set; } = string.Empty;

    /// <summary>
    /// 权限级别：read/write/admin
    /// </summary>
    public string Permission { get; set; } = "read";

    /// <summary>
    /// 过期时间（可选）
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// 取消授权请求参数
/// </summary>
public class RevokeAuthorizationRequest
{
    /// <summary>
    /// 目录路径
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>
    /// 要取消授权的用户名
    /// </summary>
    public string AuthorizedUsername { get; set; } = string.Empty;
}

#endregion


