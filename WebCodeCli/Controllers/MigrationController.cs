using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SqlSugar;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.SessionShare;
using WebCodeCli.Domain.Repositories.Base.ChatSession;
using WebCodeCli.Domain.Repositories.Base.SessionOutput;
using WebCodeCli.Domain.Repositories.Base.Template;
using WebCodeCli.Domain.Repositories.Base.InputHistory;
using WebCodeCli.Domain.Repositories.Base.QuickAction;
using WebCodeCli.Domain.Repositories.Base.UserSetting;

namespace WebCodeCli.Controllers;

/// <summary>
/// 数据库迁移控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = UserAccessConstants.AdminRole)]
public class MigrationController : ControllerBase
{
    private readonly ILogger<MigrationController> _logger;
    private readonly ISessionShareRepository _sessionShareRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IChatMessageRepository _chatMessageRepository;
    private readonly ISessionOutputRepository _sessionOutputRepository;
    private readonly IPromptTemplateRepository _templateRepository;
    private readonly IInputHistoryRepository _inputHistoryRepository;
    private readonly IQuickActionRepository _quickActionRepository;
    private readonly IUserSettingRepository _userSettingRepository;
    private readonly IUserContextService _userContextService;

    public MigrationController(
        ILogger<MigrationController> logger,
        ISessionShareRepository sessionShareRepository,
        IChatSessionRepository chatSessionRepository,
        IChatMessageRepository chatMessageRepository,
        ISessionOutputRepository sessionOutputRepository,
        IPromptTemplateRepository templateRepository,
        IInputHistoryRepository inputHistoryRepository,
        IQuickActionRepository quickActionRepository,
        IUserSettingRepository userSettingRepository,
        IUserContextService userContextService)
    {
        _logger = logger;
        _sessionShareRepository = sessionShareRepository;
        _chatSessionRepository = chatSessionRepository;
        _chatMessageRepository = chatMessageRepository;
        _sessionOutputRepository = sessionOutputRepository;
        _templateRepository = templateRepository;
        _inputHistoryRepository = inputHistoryRepository;
        _quickActionRepository = quickActionRepository;
        _userSettingRepository = userSettingRepository;
        _userContextService = userContextService;
    }

    /// <summary>
    /// 手动执行SessionShare表迁移
    /// </summary>
    [HttpPost("session-share")]
    public async Task<IActionResult> MigrateSessionShare()
    {
        try
        {
            _logger.LogInformation("开始手动迁移SessionShare表...");
            
            var db = _sessionShareRepository.GetDB();
            
            // 检查表是否存在
            bool tableExists = db.DbMaintenance.IsAnyTable("SessionShare", false);
            _logger.LogInformation($"SessionShare表存在状态: {tableExists}");
            
            if (!tableExists)
            {
                _logger.LogInformation("表不存在，开始创建...");
                db.CodeFirst.InitTables<SessionShare>();
                _logger.LogInformation("SessionShare表创建成功");
                
                return Ok(new 
                { 
                    success = true, 
                    message = "SessionShare表创建成功",
                    existed = false
                });
            }
            else
            {
                _logger.LogInformation("表已存在");
                
                // 获取表结构信息
                var columns = db.DbMaintenance.GetColumnInfosByTableName("SessionShare", false);
                
                return Ok(new 
                { 
                    success = true, 
                    message = "SessionShare表已存在",
                    existed = true,
                    columnCount = columns.Count
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "迁移SessionShare表失败");
            return StatusCode(500, new 
            { 
                success = false, 
                message = $"迁移失败: {ex.Message}",
                stackTrace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// 检查所有表状态
    /// </summary>
    [HttpGet("check-tables")]
    public IActionResult CheckTables()
    {
        try
        {
            var db = _sessionShareRepository.GetDB();
            var tables = db.DbMaintenance.GetTableInfoList(false);
            
            return Ok(new 
            { 
                success = true, 
                tableCount = tables.Count,
                tables = tables.Select(t => t.Name).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查表状态失败");
            return StatusCode(500, new 
            { 
                success = false, 
                message = $"检查失败: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// 重建SessionShare表（危险操作，会删除现有数据）
    /// </summary>
    [HttpPost("rebuild-session-share")]
    public IActionResult RebuildSessionShare()
    {
        try
        {
            _logger.LogWarning("开始重建SessionShare表（将删除现有数据）...");
            
            var db = _sessionShareRepository.GetDB();
            
            // 删除表
            if (db.DbMaintenance.IsAnyTable("SessionShare", false))
            {
                db.DbMaintenance.DropTable("SessionShare");
                _logger.LogInformation("SessionShare表已删除");
            }
            
            // 重新创建
            db.CodeFirst.InitTables<SessionShare>();
            _logger.LogInformation("SessionShare表已重建");
            
            return Ok(new 
            { 
                success = true, 
                message = "SessionShare表重建成功"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重建SessionShare表失败");
            return StatusCode(500, new 
            { 
                success = false, 
                message = $"重建失败: {ex.Message}",
                stackTrace = ex.StackTrace
            });
        }
    }

    #region IndexedDB 数据迁移

    /// <summary>
    /// 从 IndexedDB 迁移会话数据
    /// </summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> MigrateSessions([FromBody] List<MigrationSessionDto> sessions)
    {
        try
        {
            if (sessions == null || sessions.Count == 0)
            {
                return Ok(new { success = true, message = "没有需要迁移的会话", migratedCount = 0 });
            }

            var username = _userContextService.GetCurrentUsername();
            _logger.LogInformation("开始迁移 {Count} 个会话，用户: {Username}", sessions.Count, username);

            var migratedCount = 0;
            var errorCount = 0;

            foreach (var sessionDto in sessions)
            {
                try
                {
                    // 检查是否已存在
                    var existing = await _chatSessionRepository.GetByIdAndUsernameAsync(sessionDto.SessionId, username);
                    if (existing != null)
                    {
                        _logger.LogDebug("会话 {SessionId} 已存在，跳过", sessionDto.SessionId);
                        continue;
                    }

                    // 保存会话
                    var sessionEntity = new ChatSessionEntity
                    {
                        SessionId = sessionDto.SessionId,
                        Username = username,
                        Title = sessionDto.Title ?? "新会话",
                        WorkspacePath = sessionDto.WorkspacePath,
                        ToolId = sessionDto.ToolId,
                        CreatedAt = sessionDto.CreatedAt,
                        UpdatedAt = sessionDto.UpdatedAt,
                        IsWorkspaceValid = sessionDto.IsWorkspaceValid
                    };
                    await _chatSessionRepository.InsertAsync(sessionEntity);

                    // 保存消息
                    if (sessionDto.Messages != null && sessionDto.Messages.Count > 0)
                    {
                        var messageEntities = sessionDto.Messages.Select(m => new ChatMessageEntity
                        {
                            SessionId = sessionDto.SessionId,
                            Username = username,
                            Role = m.Role ?? "user",
                            Content = m.Content,
                            CreatedAt = m.CreatedAt
                        }).ToList();
                        await _chatMessageRepository.InsertMessagesAsync(messageEntities);
                    }

                    migratedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "迁移会话 {SessionId} 失败", sessionDto.SessionId);
                    errorCount++;
                }
            }

            _logger.LogInformation("会话迁移完成: 成功 {Success} 个, 失败 {Failed} 个", migratedCount, errorCount);

            return Ok(new 
            { 
                success = true, 
                message = $"迁移完成: {migratedCount} 个会话",
                migratedCount,
                errorCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "迁移会话失败");
            return StatusCode(500, new { success = false, message = $"迁移失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 从 IndexedDB 迁移模板数据
    /// </summary>
    [HttpPost("templates")]
    public async Task<IActionResult> MigrateTemplates([FromBody] List<MigrationTemplateDto> templates)
    {
        try
        {
            if (templates == null || templates.Count == 0)
            {
                return Ok(new { success = true, message = "没有需要迁移的模板", migratedCount = 0 });
            }

            var username = _userContextService.GetCurrentUsername();
            _logger.LogInformation("开始迁移 {Count} 个模板，用户: {Username}", templates.Count, username);

            var migratedCount = 0;

            foreach (var templateDto in templates)
            {
                try
                {
                    var existing = await _templateRepository.GetByIdAndUsernameAsync(templateDto.Id, username);
                    if (existing != null)
                    {
                        continue;
                    }

                    var entity = new PromptTemplateEntity
                    {
                        Id = templateDto.Id,
                        Username = username,
                        Title = templateDto.Title,
                        Content = templateDto.Content,
                        Category = templateDto.Category,
                        Icon = templateDto.Icon,
                        IsCustom = templateDto.IsCustom,
                        IsFavorite = templateDto.IsFavorite,
                        VariablesJson = templateDto.Variables != null ? JsonSerializer.Serialize(templateDto.Variables) : null,
                        CreatedAt = templateDto.CreatedAt,
                        UpdatedAt = templateDto.UpdatedAt
                    };
                    await _templateRepository.InsertAsync(entity);
                    migratedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "迁移模板 {Id} 失败", templateDto.Id);
                }
            }

            return Ok(new { success = true, message = $"迁移完成: {migratedCount} 个模板", migratedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "迁移模板失败");
            return StatusCode(500, new { success = false, message = $"迁移失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 从 IndexedDB 迁移会话输出状态
    /// </summary>
    [HttpPost("session-outputs")]
    public async Task<IActionResult> MigrateSessionOutputs([FromBody] List<MigrationOutputDto> outputs)
    {
        try
        {
            if (outputs == null || outputs.Count == 0)
            {
                return Ok(new { success = true, message = "没有需要迁移的输出状态", migratedCount = 0 });
            }

            var username = _userContextService.GetCurrentUsername();
            _logger.LogInformation("开始迁移 {Count} 个输出状态，用户: {Username}", outputs.Count, username);

            var migratedCount = 0;

            foreach (var outputDto in outputs)
            {
                try
                {
                    var existing = await _sessionOutputRepository.GetBySessionIdAndUsernameAsync(outputDto.SessionId, username);
                    if (existing != null)
                    {
                        continue;
                    }

                    var entity = new SessionOutputEntity
                    {
                        SessionId = outputDto.SessionId,
                        Username = username,
                        RawOutput = outputDto.RawOutput,
                        EventsJson = outputDto.EventsJson,
                        DisplayedEventCount = outputDto.DisplayedEventCount,
                        UpdatedAt = DateTime.Now
                    };
                    await _sessionOutputRepository.InsertAsync(entity);
                    migratedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "迁移输出状态 {SessionId} 失败", outputDto.SessionId);
                }
            }

            return Ok(new { success = true, message = $"迁移完成: {migratedCount} 个输出状态", migratedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "迁移输出状态失败");
            return StatusCode(500, new { success = false, message = $"迁移失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 从 IndexedDB 迁移输入历史
    /// </summary>
    [HttpPost("input-history")]
    public async Task<IActionResult> MigrateInputHistory([FromBody] List<MigrationInputHistoryDto> history)
    {
        try
        {
            if (history == null || history.Count == 0)
            {
                return Ok(new { success = true, message = "没有需要迁移的输入历史", migratedCount = 0 });
            }

            var username = _userContextService.GetCurrentUsername();
            _logger.LogInformation("开始迁移 {Count} 条输入历史，用户: {Username}", history.Count, username);

            var migratedCount = 0;

            foreach (var item in history)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(item.Text))
                        continue;

                    var entity = new InputHistoryEntity
                    {
                        Username = username,
                        Text = item.Text,
                        Timestamp = item.Timestamp
                    };
                    await _inputHistoryRepository.InsertAsync(entity);
                    migratedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "迁移输入历史失败");
                }
            }

            return Ok(new { success = true, message = $"迁移完成: {migratedCount} 条输入历史", migratedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "迁移输入历史失败");
            return StatusCode(500, new { success = false, message = $"迁移失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 从 IndexedDB 迁移快捷操作
    /// </summary>
    [HttpPost("quick-actions")]
    public async Task<IActionResult> MigrateQuickActions([FromBody] List<MigrationQuickActionDto> actions)
    {
        try
        {
            if (actions == null || actions.Count == 0)
            {
                return Ok(new { success = true, message = "没有需要迁移的快捷操作", migratedCount = 0 });
            }

            var username = _userContextService.GetCurrentUsername();
            _logger.LogInformation("开始迁移 {Count} 个快捷操作，用户: {Username}", actions.Count, username);

            var entities = actions.Select(a => new QuickActionEntity
            {
                Id = a.Id ?? Guid.NewGuid().ToString(),
                Username = username,
                Title = a.Title,
                Icon = a.Icon,
                Prompt = a.Content ?? a.Prompt,
                Order = a.Order,
                IsEnabled = a.IsEnabled
            }).ToList();

            await _quickActionRepository.SaveAllAsync(username, entities);

            return Ok(new { success = true, message = $"迁移完成: {entities.Count} 个快捷操作", migratedCount = entities.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "迁移快捷操作失败");
            return StatusCode(500, new { success = false, message = $"迁移失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 从 IndexedDB 迁移设置
    /// </summary>
    [HttpPost("settings")]
    public async Task<IActionResult> MigrateSettings([FromBody] Dictionary<string, string?> settings)
    {
        try
        {
            if (settings == null || settings.Count == 0)
            {
                return Ok(new { success = true, message = "没有需要迁移的设置", migratedCount = 0 });
            }

            var username = _userContextService.GetCurrentUsername();
            _logger.LogInformation("开始迁移 {Count} 个设置，用户: {Username}", settings.Count, username);

            var migratedCount = 0;

            foreach (var kvp in settings)
            {
                try
                {
                    await _userSettingRepository.SetValueAsync(username, kvp.Key, kvp.Value);
                    migratedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "迁移设置 {Key} 失败", kvp.Key);
                }
            }

            return Ok(new { success = true, message = $"迁移完成: {migratedCount} 个设置", migratedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "迁移设置失败");
            return StatusCode(500, new { success = false, message = $"迁移失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 检查迁移状态
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetMigrationStatus()
    {
        try
        {
            var username = _userContextService.GetCurrentUsername();
            
            var sessionCount = await _chatSessionRepository.CountAsync(x => x.Username == username);
            var templateCount = await _templateRepository.CountAsync(x => x.Username == username);
            var outputCount = await _sessionOutputRepository.CountAsync(x => x.Username == username);
            var historyCount = await _inputHistoryRepository.CountAsync(x => x.Username == username);
            var actionCount = await _quickActionRepository.CountAsync(x => x.Username == username);
            var settingCount = await _userSettingRepository.CountAsync(x => x.Username == username);

            return Ok(new 
            { 
                success = true, 
                username,
                counts = new
                {
                    sessions = sessionCount,
                    templates = templateCount,
                    outputs = outputCount,
                    inputHistory = historyCount,
                    quickActions = actionCount,
                    settings = settingCount
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取迁移状态失败");
            return StatusCode(500, new { success = false, message = $"获取状态失败: {ex.Message}" });
        }
    }

    #endregion
}

#region 迁移 DTO

public class MigrationSessionDto
{
    public string SessionId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? WorkspacePath { get; set; }
    public string? ToolId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsWorkspaceValid { get; set; }
    public List<MigrationMessageDto>? Messages { get; set; }
}

public class MigrationMessageDto
{
    public string? Role { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MigrationTemplateDto
{
    public string Id { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? Category { get; set; }
    public string? Icon { get; set; }
    public bool IsCustom { get; set; }
    public bool IsFavorite { get; set; }
    public List<string>? Variables { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MigrationOutputDto
{
    public string SessionId { get; set; } = string.Empty;
    public string? RawOutput { get; set; }
    public string? EventsJson { get; set; }
    public int DisplayedEventCount { get; set; }
}

public class MigrationInputHistoryDto
{
    public string? Text { get; set; }
    public DateTime Timestamp { get; set; }
}

public class MigrationQuickActionDto
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Icon { get; set; }
    public string? Content { get; set; }
    public string? Prompt { get; set; }
    public int Order { get; set; }
    public bool IsEnabled { get; set; } = true;
}

#endregion
