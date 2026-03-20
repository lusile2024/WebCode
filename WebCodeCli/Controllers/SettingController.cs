using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Controllers;

/// <summary>
/// 设置管理 API 控制器
/// </summary>
[ApiController]
[Route("api/setting")]
[Authorize]
public class SettingController : ControllerBase
{
    private readonly IUserSettingService _settingService;
    private readonly IInputHistoryService _inputHistoryService;
    private readonly IQuickActionService _quickActionService;
    private readonly ICliExecutorService _cliExecutorService;
    private readonly IUserContextService _userContextService;
    private readonly ILogger<SettingController> _logger;

    public SettingController(
        IUserSettingService settingService,
        IInputHistoryService inputHistoryService,
        IQuickActionService quickActionService,
        ICliExecutorService cliExecutorService,
        IUserContextService userContextService,
        ILogger<SettingController> logger)
    {
        _settingService = settingService;
        _inputHistoryService = inputHistoryService;
        _quickActionService = quickActionService;
        _cliExecutorService = cliExecutorService;
        _userContextService = userContextService;
        _logger = logger;
    }

    #region CLI 工具

    /// <summary>
    /// 获取所有可用的 CLI 工具
    /// </summary>
    [HttpGet("tools")]
    public ActionResult<List<CliToolInfo>> GetTools()
    {
        try
        {
            var tools = _cliExecutorService.GetAvailableTools(_userContextService.GetCurrentUsername());
            var result = tools.Select(t => new CliToolInfo
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                Enabled = t.Enabled
            }).ToList();
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取 CLI 工具列表失败");
            return StatusCode(500, new { Error = "获取 CLI 工具列表失败" });
        }
    }

    #endregion

    #region 用户设置

    /// <summary>
    /// 获取所有设置
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<Dictionary<string, string?>>> GetAllSettings()
    {
        try
        {
            var settings = await _settingService.GetAllAsync();
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取设置失败");
            return StatusCode(500, new { Error = "获取设置失败" });
        }
    }

    /// <summary>
    /// 获取单个设置
    /// </summary>
    [HttpGet("{key}")]
    public async Task<ActionResult<SettingValueDto>> GetSetting(string key)
    {
        try
        {
            var value = await _settingService.GetAsync(key);
            return Ok(new SettingValueDto { Key = key, Value = value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取设置失败: {Key}", key);
            return StatusCode(500, new { Error = "获取设置失败" });
        }
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    [HttpPut("{key}")]
    public async Task<ActionResult> SaveSetting(string key, [FromBody] SettingValueDto dto)
    {
        try
        {
            var success = await _settingService.SetAsync(key, dto?.Value);
            
            if (success)
            {
                return Ok(new { Success = true });
            }
            else
            {
                return StatusCode(500, new { Error = "保存设置失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存设置失败: {Key}", key);
            return StatusCode(500, new { Error = "保存设置失败" });
        }
    }

    /// <summary>
    /// 删除设置
    /// </summary>
    [HttpDelete("{key}")]
    public async Task<ActionResult> DeleteSetting(string key)
    {
        try
        {
            var success = await _settingService.DeleteAsync(key);
            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除设置失败: {Key}", key);
            return StatusCode(500, new { Error = "删除设置失败" });
        }
    }

    #endregion

    #region 输入历史

    /// <summary>
    /// 获取输入历史
    /// </summary>
    [HttpGet("input-history")]
    public async Task<ActionResult<List<InputHistoryItem>>> GetInputHistory([FromQuery] int limit = 50)
    {
        try
        {
            var history = await _inputHistoryService.GetRecentAsync(limit);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取输入历史失败");
            return StatusCode(500, new { Error = "获取输入历史失败" });
        }
    }

    /// <summary>
    /// 搜索输入历史
    /// </summary>
    [HttpGet("input-history/search")]
    public async Task<ActionResult<List<InputHistoryItem>>> SearchInputHistory([FromQuery] string q, [FromQuery] int limit = 10)
    {
        try
        {
            var history = await _inputHistoryService.SearchAsync(q, limit);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "搜索输入历史失败: {Query}", q);
            return StatusCode(500, new { Error = "搜索输入历史失败" });
        }
    }

    /// <summary>
    /// 保存输入历史
    /// </summary>
    [HttpPost("input-history")]
    public async Task<ActionResult> SaveInputHistory([FromBody] InputHistoryDto dto)
    {
        try
        {
            var success = await _inputHistoryService.SaveAsync(dto?.Text ?? string.Empty);
            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存输入历史失败");
            return StatusCode(500, new { Error = "保存输入历史失败" });
        }
    }

    /// <summary>
    /// 清空输入历史
    /// </summary>
    [HttpDelete("input-history")]
    public async Task<ActionResult> ClearInputHistory()
    {
        try
        {
            var success = await _inputHistoryService.ClearAsync();
            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清空输入历史失败");
            return StatusCode(500, new { Error = "清空输入历史失败" });
        }
    }

    #endregion

    #region 快捷操作

    /// <summary>
    /// 获取所有快捷操作
    /// </summary>
    [HttpGet("quick-actions")]
    public async Task<ActionResult<List<QuickAction>>> GetQuickActions()
    {
        try
        {
            var actions = await _quickActionService.GetAllAsync();
            return Ok(actions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取快捷操作失败");
            return StatusCode(500, new { Error = "获取快捷操作失败" });
        }
    }

    /// <summary>
    /// 保存快捷操作
    /// </summary>
    [HttpPost("quick-actions")]
    public async Task<ActionResult> SaveQuickAction([FromBody] QuickAction action)
    {
        try
        {
            if (action == null || string.IsNullOrWhiteSpace(action.Id))
            {
                return BadRequest(new { Error = "无效的快捷操作数据" });
            }
            
            var success = await _quickActionService.SaveAsync(action);
            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存快捷操作失败: {Id}", action?.Id);
            return StatusCode(500, new { Error = "保存快捷操作失败" });
        }
    }

    /// <summary>
    /// 批量保存快捷操作
    /// </summary>
    [HttpPut("quick-actions")]
    public async Task<ActionResult> SaveAllQuickActions([FromBody] List<QuickAction> actions)
    {
        try
        {
            var success = await _quickActionService.SaveAllAsync(actions ?? new List<QuickAction>());
            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量保存快捷操作失败");
            return StatusCode(500, new { Error = "批量保存快捷操作失败" });
        }
    }

    /// <summary>
    /// 删除快捷操作
    /// </summary>
    [HttpDelete("quick-actions/{id}")]
    public async Task<ActionResult> DeleteQuickAction(string id)
    {
        try
        {
            var success = await _quickActionService.DeleteAsync(id);
            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除快捷操作失败: {Id}", id);
            return StatusCode(500, new { Error = "删除快捷操作失败" });
        }
    }

    /// <summary>
    /// 清空所有快捷操作
    /// </summary>
    [HttpDelete("quick-actions")]
    public async Task<ActionResult> ClearQuickActions()
    {
        try
        {
            var success = await _quickActionService.ClearAsync();
            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清空快捷操作失败");
            return StatusCode(500, new { Error = "清空快捷操作失败" });
        }
    }

    #endregion
}

/// <summary>
/// 设置值 DTO
/// </summary>
public class SettingValueDto
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>
/// 输入历史 DTO
/// </summary>
public class InputHistoryDto
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// CLI 工具信息 DTO
/// </summary>
public class CliToolInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
