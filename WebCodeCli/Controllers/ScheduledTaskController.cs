using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.ScheduledTask;

namespace WebCodeCli.Controllers;

/// <summary>
/// 定时任务管理 API 控制器
/// </summary>
[ApiController]
[Route("api/scheduled-task")]
[Authorize]
public class ScheduledTaskController : ControllerBase
{
    private readonly IScheduledTaskService _taskService;
    private readonly IUserContextService _userContextService;
    private readonly ILogger<ScheduledTaskController> _logger;

    public ScheduledTaskController(
        IScheduledTaskService taskService,
        IUserContextService userContextService,
        ILogger<ScheduledTaskController> logger)
    {
        _taskService = taskService;
        _userContextService = userContextService;
        _logger = logger;
    }

    private string GetCurrentUsername()
    {
        return _userContextService.GetCurrentUsername();
    }

    #region 任务管理

    /// <summary>
    /// 获取所有定时任务
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ScheduledTaskEntity>>> GetTasks()
    {
        try
        {
            var tasks = await _taskService.GetTasksAsync(GetCurrentUsername());
            return Ok(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取定时任务列表失败");
            return StatusCode(500, new { Error = "获取定时任务列表失败" });
        }
    }

    /// <summary>
    /// 获取单个定时任务
    /// </summary>
    [HttpGet("{taskId}")]
    public async Task<ActionResult<ScheduledTaskEntity>> GetTask(string taskId)
    {
        try
        {
            var task = await _taskService.GetTaskByIdAsync(taskId, GetCurrentUsername());
            
            if (task == null)
            {
                return NotFound(new { Error = "任务不存在" });
            }
            
            return Ok(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取定时任务失败: {TaskId}", taskId);
            return StatusCode(500, new { Error = "获取定时任务失败" });
        }
    }

    /// <summary>
    /// 创建定时任务
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ScheduledTaskEntity>> CreateTask([FromBody] CreateScheduledTaskRequest request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(new { Error = "无效的请求数据" });
            }
            
            var (success, errorMessage, task) = await _taskService.CreateTaskAsync(
                GetCurrentUsername(),
                request.Name,
                request.Description,
                request.CronExpression,
                request.ToolId,
                request.Prompt,
                request.SessionId,
                request.TimeoutSeconds,
                request.MaxRetries,
                request.IsEnabled);
            
            if (!success || task == null)
            {
                return BadRequest(new { Error = errorMessage ?? "创建任务失败" });
            }
            
            return Ok(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建定时任务失败");
            return StatusCode(500, new { Error = "创建定时任务失败" });
        }
    }

    /// <summary>
    /// 更新定时任务
    /// </summary>
    [HttpPut("{taskId}")]
    public async Task<ActionResult> UpdateTask(string taskId, [FromBody] UpdateScheduledTaskRequest request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(new { Error = "无效的请求数据" });
            }
            
            var (success, errorMessage) = await _taskService.UpdateTaskAsync(
                taskId,
                GetCurrentUsername(),
                request.Name,
                request.Description,
                request.CronExpression,
                request.ToolId,
                request.Prompt,
                request.SessionId,
                request.TimeoutSeconds,
                request.MaxRetries,
                request.IsEnabled);
            
            if (!success)
            {
                return BadRequest(new { Error = errorMessage ?? "更新任务失败" });
            }
            
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新定时任务失败: {TaskId}", taskId);
            return StatusCode(500, new { Error = "更新定时任务失败" });
        }
    }

    /// <summary>
    /// 删除定时任务
    /// </summary>
    [HttpDelete("{taskId}")]
    public async Task<ActionResult> DeleteTask(string taskId, [FromQuery] bool deleteExecutions = true)
    {
        try
        {
            var (success, errorMessage) = await _taskService.DeleteTaskAsync(taskId, GetCurrentUsername(), deleteExecutions);
            
            if (!success)
            {
                return BadRequest(new { Error = errorMessage ?? "删除任务失败" });
            }
            
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除定时任务失败: {TaskId}", taskId);
            return StatusCode(500, new { Error = "删除定时任务失败" });
        }
    }

    /// <summary>
    /// 启用/禁用定时任务
    /// </summary>
    [HttpPost("{taskId}/toggle")]
    public async Task<ActionResult> ToggleTask(string taskId, [FromBody] ToggleTaskRequest request)
    {
        try
        {
            var (success, errorMessage) = await _taskService.ToggleTaskAsync(taskId, GetCurrentUsername(), request.IsEnabled);
            
            if (!success)
            {
                return BadRequest(new { Error = errorMessage ?? "切换任务状态失败" });
            }
            
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换定时任务状态失败: {TaskId}", taskId);
            return StatusCode(500, new { Error = "切换任务状态失败" });
        }
    }

    #endregion

    #region 任务执行

    /// <summary>
    /// 立即执行任务
    /// </summary>
    [HttpPost("{taskId}/trigger")]
    public async Task<ActionResult> TriggerTask(string taskId)
    {
        try
        {
            var (success, errorMessage, executionId) = await _taskService.TriggerTaskAsync(taskId, GetCurrentUsername());
            
            if (!success)
            {
                return BadRequest(new { Error = errorMessage ?? "触发任务失败", ExecutionId = executionId });
            }
            
            return Ok(new { Success = true, ExecutionId = executionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发定时任务失败: {TaskId}", taskId);
            return StatusCode(500, new { Error = "触发任务失败" });
        }
    }

    /// <summary>
    /// 取消正在执行的任务
    /// </summary>
    [HttpPost("executions/{executionId}/cancel")]
    public async Task<ActionResult> CancelExecution(long executionId)
    {
        try
        {
            var (success, errorMessage) = await _taskService.CancelExecutionAsync(executionId);
            
            if (!success)
            {
                return BadRequest(new { Error = errorMessage ?? "取消执行失败" });
            }
            
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消任务执行失败: {ExecutionId}", executionId);
            return StatusCode(500, new { Error = "取消执行失败" });
        }
    }

    #endregion

    #region 执行记录

    /// <summary>
    /// 获取任务的执行记录
    /// </summary>
    [HttpGet("{taskId}/executions")]
    public async Task<ActionResult<List<TaskExecutionEntity>>> GetExecutions(string taskId, [FromQuery] int limit = 50)
    {
        try
        {
            var executions = await _taskService.GetExecutionsAsync(taskId, GetCurrentUsername(), limit);
            return Ok(executions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取执行记录失败: {TaskId}", taskId);
            return StatusCode(500, new { Error = "获取执行记录失败" });
        }
    }

    /// <summary>
    /// 获取单个执行记录详情
    /// </summary>
    [HttpGet("executions/{executionId}")]
    public async Task<ActionResult<TaskExecutionEntity>> GetExecution(long executionId)
    {
        try
        {
            var execution = await _taskService.GetExecutionByIdAsync(executionId);
            
            if (execution == null)
            {
                return NotFound(new { Error = "执行记录不存在" });
            }
            
            return Ok(execution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取执行记录详情失败: {ExecutionId}", executionId);
            return StatusCode(500, new { Error = "获取执行记录详情失败" });
        }
    }

    /// <summary>
    /// 清理任务的旧执行记录
    /// </summary>
    [HttpPost("{taskId}/executions/cleanup")]
    public async Task<ActionResult> CleanupExecutions(string taskId, [FromQuery] int keepCount = 100)
    {
        try
        {
            var deletedCount = await _taskService.CleanupExecutionsAsync(taskId, GetCurrentUsername(), keepCount);
            return Ok(new { Success = true, DeletedCount = deletedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理执行记录失败: {TaskId}", taskId);
            return StatusCode(500, new { Error = "清理执行记录失败" });
        }
    }

    #endregion

    #region Cron 表达式

    /// <summary>
    /// 验证 Cron 表达式
    /// </summary>
    [HttpPost("validate-cron")]
    public ActionResult ValidateCron([FromBody] ValidateCronRequest request)
    {
        try
        {
            var (isValid, errorMessage) = _taskService.ValidateCronExpression(request.CronExpression);
            
            if (!isValid)
            {
                return Ok(new { IsValid = false, Error = errorMessage });
            }
            
            var nextFireTimes = _taskService.GetNextFireTimes(request.CronExpression, 5);
            
            return Ok(new { IsValid = true, NextFireTimes = nextFireTimes });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证 Cron 表达式失败");
            return StatusCode(500, new { Error = "验证失败" });
        }
    }

    /// <summary>
    /// 获取 Cron 表达式预设
    /// </summary>
    [HttpGet("cron-presets")]
    public ActionResult<List<CronPreset>> GetCronPresets()
    {
        try
        {
            var presets = _taskService.GetCronPresets();
            return Ok(presets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取 Cron 预设失败");
            return StatusCode(500, new { Error = "获取 Cron 预设失败" });
        }
    }

    #endregion
}

#region 请求模型

/// <summary>
/// 创建定时任务请求
/// </summary>
public class CreateScheduledTaskRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int TimeoutSeconds { get; set; } = 3600;
    public int MaxRetries { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 更新定时任务请求
/// </summary>
public class UpdateScheduledTaskRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int TimeoutSeconds { get; set; } = 3600;
    public int MaxRetries { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 切换任务状态请求
/// </summary>
public class ToggleTaskRequest
{
    public bool IsEnabled { get; set; }
}

/// <summary>
/// 验证 Cron 表达式请求
/// </summary>
public class ValidateCronRequest
{
    public string CronExpression { get; set; } = string.Empty;
}

#endregion
