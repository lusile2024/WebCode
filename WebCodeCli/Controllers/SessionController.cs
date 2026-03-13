using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Controllers;

/// <summary>
/// 会话管理 API 控制器
/// </summary>
[ApiController]
[Route("api/session")]
public class SessionController : ControllerBase
{
    private readonly ISessionHistoryManager _sessionHistoryManager;
    private readonly ISessionOutputService _sessionOutputService;
    private readonly ISessionDirectoryService _sessionDirectoryService;
    private readonly ILogger<SessionController> _logger;

    public SessionController(
        ISessionHistoryManager sessionHistoryManager,
        ISessionOutputService sessionOutputService,
        ISessionDirectoryService sessionDirectoryService,
        ILogger<SessionController> logger)
    {
        _sessionHistoryManager = sessionHistoryManager;
        _sessionOutputService = sessionOutputService;
        _sessionDirectoryService = sessionDirectoryService;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前用户名
    /// </summary>
    private string GetCurrentUsername()
    {
        return User.FindFirstValue(ClaimTypes.Name) ?? "default";
    }

    /// <summary>
    /// 获取所有会话
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SessionSummaryDto>>> GetSessions()
    {
        try
        {
            var sessions = await _sessionHistoryManager.LoadSessionsAsync();
            var summaries = sessions.Select(s => new SessionSummaryDto
            {
                SessionId = s.SessionId,
                Title = s.Title,
                WorkspacePath = s.WorkspacePath,
                ToolId = s.ToolId,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
                IsWorkspaceValid = s.IsWorkspaceValid,
                MessageCount = s.Messages?.Count ?? 0,
                ProjectId = s.ProjectId,
                ProjectName = s.ProjectName
            }).ToList();
            
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取会话列表失败");
            return StatusCode(500, new { Error = "获取会话列表失败" });
        }
    }

    /// <summary>
    /// 获取单个会话（含消息）
    /// </summary>
    [HttpGet("{sessionId}")]
    public async Task<ActionResult<SessionHistory>> GetSession(string sessionId)
    {
        try
        {
            var session = await _sessionHistoryManager.GetSessionAsync(sessionId);
            
            if (session == null)
            {
                return NotFound(new { Error = "会话不存在" });
            }
            
            return Ok(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取会话失败: {SessionId}", sessionId);
            return StatusCode(500, new { Error = "获取会话失败" });
        }
    }

    /// <summary>
    /// 创建或更新会话
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> SaveSession([FromBody] SessionHistory session)
    {
        try
        {
            if (session == null || string.IsNullOrWhiteSpace(session.SessionId))
            {
                return BadRequest(new { Error = "无效的会话数据" });
            }
            
            await _sessionHistoryManager.SaveSessionImmediateAsync(session);
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存会话失败: {SessionId}", session?.SessionId);
            return StatusCode(500, new { Error = "保存会话失败" });
        }
    }

    /// <summary>
    /// 更新会话
    /// </summary>
    [HttpPut("{sessionId}")]
    public async Task<ActionResult> UpdateSession(string sessionId, [FromBody] SessionHistory session)
    {
        try
        {
            if (session == null || sessionId != session.SessionId)
            {
                return BadRequest(new { Error = "无效的会话数据" });
            }
            
            await _sessionHistoryManager.SaveSessionImmediateAsync(session);
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新会话失败: {SessionId}", sessionId);
            return StatusCode(500, new { Error = "更新会话失败" });
        }
    }

    /// <summary>
    /// 删除会话
    /// </summary>
    [HttpDelete("{sessionId}")]
    public async Task<ActionResult> DeleteSession(string sessionId)
    {
        try
        {
            await _sessionHistoryManager.DeleteSessionAsync(sessionId);
            
            // 同时删除输出状态
            await _sessionOutputService.DeleteBySessionIdAsync(sessionId);
            
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除会话失败: {SessionId}", sessionId);
            return StatusCode(500, new { Error = "删除会话失败" });
        }
    }

    /// <summary>
    /// 获取会话输出状态
    /// </summary>
    [HttpGet("{sessionId}/output")]
    public async Task<ActionResult<OutputPanelState>> GetSessionOutput(string sessionId)
    {
        try
        {
            var output = await _sessionOutputService.GetBySessionIdAsync(sessionId);
            
            if (output == null)
            {
                return NotFound(new { Error = "输出状态不存在" });
            }
            
            return Ok(output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取会话输出状态失败: {SessionId}", sessionId);
            return StatusCode(500, new { Error = "获取输出状态失败" });
        }
    }

    /// <summary>
    /// 保存会话输出状态
    /// </summary>
    [HttpPut("{sessionId}/output")]
    public async Task<ActionResult> SaveSessionOutput(string sessionId, [FromBody] OutputPanelState state)
    {
        try
        {
            if (state == null)
            {
                return BadRequest(new { Error = "无效的输出状态数据" });
            }
            
            state.SessionId = sessionId;
            var success = await _sessionOutputService.SaveAsync(state);
            
            if (success)
            {
                return Ok(new { Success = true });
            }
            else
            {
                return StatusCode(500, new { Error = "保存输出状态失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存会话输出状态失败: {SessionId}", sessionId);
            return StatusCode(500, new { Error = "保存输出状态失败" });
        }
    }

    #region 会话工作区管理API

    /// <summary>
    /// 更新会话的工作区目录
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="request">更新请求参数</param>
    [HttpPut("{sessionId}/workspace")]
    public async Task<IActionResult> UpdateSessionWorkspace(string sessionId, [FromBody] UpdateSessionWorkspaceRequest request)
    {
        try
        {
            // 已禁用切换目录功能：会话绑定后不允许更改工作目录
            return BadRequest(new { error = "会话目录已绑定，不允许切换。如需使用其他目录，请创建新会话。" });

            // var currentUser = GetCurrentUsername();
            // await _sessionDirectoryService.SwitchSessionWorkspaceAsync(
            //     sessionId,
            //     currentUser,
            //     request.DirectoryPath);
            //
            // return Ok(new { success = true });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新会话工作区失败: {SessionId}", sessionId);
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取会话的工作区信息
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    [HttpGet("{sessionId}/workspace")]
    public async Task<IActionResult> GetSessionWorkspace(string sessionId)
    {
        try
        {
            var currentUser = GetCurrentUsername();
            var workspacePath = await _sessionDirectoryService.GetSessionWorkspaceAsync(sessionId, currentUser);

            return Ok(new
            {
                success = true,
                data = new
                {
                    sessionId,
                    workspacePath
                }
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取会话工作区失败: {SessionId}", sessionId);
            return StatusCode(500, new { error = "服务器错误", message = ex.Message });
        }
    }

    #endregion
}

/// <summary>
/// 更新会话工作区请求参数
/// </summary>
public class UpdateSessionWorkspaceRequest
{
    /// <summary>
    /// 新的工作区目录路径
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;
}

/// <summary>
/// 会话摘要 DTO
/// </summary>
public class SessionSummaryDto
{
    public string SessionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsWorkspaceValid { get; set; }
    public int MessageCount { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
}
