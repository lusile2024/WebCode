using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Controllers;

/// <summary>
/// 模板管理 API 控制器
/// </summary>
[ApiController]
[Route("api/template")]
[Authorize]
public class TemplateController : ControllerBase
{
    private readonly IPromptTemplateService _templateService;
    private readonly ILogger<TemplateController> _logger;

    public TemplateController(
        IPromptTemplateService templateService,
        ILogger<TemplateController> logger)
    {
        _templateService = templateService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有模板
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<PromptTemplate>>> GetTemplates()
    {
        try
        {
            var templates = await _templateService.GetAllAsync();
            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取模板列表失败");
            return StatusCode(500, new { Error = "获取模板列表失败" });
        }
    }

    /// <summary>
    /// 根据分类获取模板
    /// </summary>
    [HttpGet("category/{category}")]
    public async Task<ActionResult<List<PromptTemplate>>> GetTemplatesByCategory(string category)
    {
        try
        {
            var templates = await _templateService.GetByCategoryAsync(category);
            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "根据分类获取模板失败: {Category}", category);
            return StatusCode(500, new { Error = "获取模板失败" });
        }
    }

    /// <summary>
    /// 获取单个模板
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PromptTemplate>> GetTemplate(string id)
    {
        try
        {
            var template = await _templateService.GetByIdAsync(id);
            
            if (template == null)
            {
                return NotFound(new { Error = "模板不存在" });
            }
            
            return Ok(template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取模板失败: {Id}", id);
            return StatusCode(500, new { Error = "获取模板失败" });
        }
    }

    /// <summary>
    /// 创建或更新模板
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> SaveTemplate([FromBody] PromptTemplate template)
    {
        try
        {
            if (template == null || string.IsNullOrWhiteSpace(template.Id))
            {
                return BadRequest(new { Error = "无效的模板数据" });
            }
            
            var success = await _templateService.SaveAsync(template);
            
            if (success)
            {
                return Ok(new { Success = true });
            }
            else
            {
                return StatusCode(500, new { Error = "保存模板失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存模板失败: {Id}", template?.Id);
            return StatusCode(500, new { Error = "保存模板失败" });
        }
    }

    /// <summary>
    /// 删除模板
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteTemplate(string id)
    {
        try
        {
            var success = await _templateService.DeleteAsync(id);
            
            if (success)
            {
                return Ok(new { Success = true });
            }
            else
            {
                return NotFound(new { Error = "模板不存在" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除模板失败: {Id}", id);
            return StatusCode(500, new { Error = "删除模板失败" });
        }
    }

    /// <summary>
    /// 获取收藏模板
    /// </summary>
    [HttpGet("favorites")]
    public async Task<ActionResult<List<PromptTemplate>>> GetFavorites()
    {
        try
        {
            var templates = await _templateService.GetFavoritesAsync();
            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取收藏模板失败");
            return StatusCode(500, new { Error = "获取收藏模板失败" });
        }
    }

    /// <summary>
    /// 初始化默认模板
    /// </summary>
    [HttpPost("init-defaults")]
    public async Task<ActionResult> InitDefaultTemplates()
    {
        try
        {
            var success = await _templateService.InitDefaultTemplatesAsync();
            
            if (success)
            {
                return Ok(new { Success = true });
            }
            else
            {
                return StatusCode(500, new { Error = "初始化默认模板失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化默认模板失败");
            return StatusCode(500, new { Error = "初始化默认模板失败" });
        }
    }
}
