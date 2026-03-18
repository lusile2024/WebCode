using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.UserAccount;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Controllers;

[ApiController]
[Authorize(Roles = UserAccessConstants.AdminRole)]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IUserAccountService _userAccountService;
    private readonly IUserToolPolicyService _userToolPolicyService;
    private readonly IUserWorkspacePolicyService _userWorkspacePolicyService;
    private readonly IUserFeishuBotConfigService _userFeishuBotConfigService;
    private readonly ICliExecutorService _cliExecutorService;

    public AdminController(
        IUserAccountService userAccountService,
        IUserToolPolicyService userToolPolicyService,
        IUserWorkspacePolicyService userWorkspacePolicyService,
        IUserFeishuBotConfigService userFeishuBotConfigService,
        ICliExecutorService cliExecutorService)
    {
        _userAccountService = userAccountService;
        _userToolPolicyService = userToolPolicyService;
        _userWorkspacePolicyService = userWorkspacePolicyService;
        _userFeishuBotConfigService = userFeishuBotConfigService;
        _cliExecutorService = cliExecutorService;
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<UserAccountResponseDto>>> GetUsers()
    {
        var users = await _userAccountService.GetAllAsync();
        return Ok(users.Select(MapUser).ToList());
    }

    [HttpPost("users")]
    public async Task<ActionResult<UserAccountResponseDto>> SaveUser([FromBody] SaveUserRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return BadRequest(new { error = "用户名不能为空。" });
        }

        var existing = await _userAccountService.GetByUsernameAsync(request.Username);
        if (existing == null && string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "新建用户时必须提供密码。" });
        }

        var saved = await _userAccountService.CreateOrUpdateAsync(new UserAccountEntity
        {
            Username = request.Username.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Username.Trim() : request.DisplayName.Trim(),
            Role = UserAccessConstants.NormalizeRole(request.Role),
            Status = UserAccessConstants.NormalizeStatus(request.Status)
        }, string.IsNullOrWhiteSpace(request.Password) ? null : request.Password, overwritePassword: !string.IsNullOrWhiteSpace(request.Password));

        if (saved == null)
        {
            return StatusCode(500, new { error = "保存用户失败。" });
        }

        return Ok(MapUser(saved));
    }

    [HttpPut("users/{username}/status")]
    public async Task<ActionResult> UpdateUserStatus(string username, [FromBody] UpdateUserStatusRequestDto request)
    {
        var existing = await _userAccountService.GetByUsernameAsync(username);
        if (existing == null)
        {
            return NotFound(new { error = "用户不存在。" });
        }

        if (!request.Enabled && string.Equals(existing.Role, UserAccessConstants.AdminRole, StringComparison.OrdinalIgnoreCase))
        {
            var users = await _userAccountService.GetAllAsync();
            var enabledAdminCount = users.Count(x =>
                string.Equals(x.Role, UserAccessConstants.AdminRole, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Status, UserAccessConstants.EnabledStatus, StringComparison.OrdinalIgnoreCase));
            if (enabledAdminCount <= 1)
            {
                return BadRequest(new { error = "系统至少需要保留一个启用的管理员。" });
            }
        }

        var success = await _userAccountService.SetStatusAsync(username, request.Enabled ? UserAccessConstants.EnabledStatus : UserAccessConstants.DisabledStatus);
        return Ok(new { success });
    }

    [HttpGet("users/{username}/tools")]
    public async Task<ActionResult<Dictionary<string, bool>>> GetUserTools(string username)
    {
        var toolIds = _cliExecutorService.GetAvailableTools().Select(x => x.Id).ToList();
        var map = await _userToolPolicyService.GetPolicyMapAsync(username, toolIds);
        return Ok(map);
    }

    [HttpPut("users/{username}/tools")]
    public async Task<ActionResult> UpdateUserTools(string username, [FromBody] UpdateUserToolsRequestDto request)
    {
        var allToolIds = _cliExecutorService.GetAvailableTools().Select(x => x.Id).ToList();
        var success = await _userToolPolicyService.SaveAllowedToolsAsync(username, request.AllowedToolIds ?? new List<string>(), allToolIds);
        return Ok(new { success });
    }

    [HttpGet("users/{username}/workspace-policies")]
    public async Task<ActionResult<List<string>>> GetWorkspacePolicies(string username)
    {
        return Ok(await _userWorkspacePolicyService.GetAllowedDirectoriesAsync(username));
    }

    [HttpPut("users/{username}/workspace-policies")]
    public async Task<ActionResult> UpdateWorkspacePolicies(string username, [FromBody] UpdateWorkspacePoliciesRequestDto request)
    {
        var success = await _userWorkspacePolicyService.SaveAllowedDirectoriesAsync(username, request.AllowedDirectories ?? new List<string>());
        return Ok(new { success });
    }

    [HttpGet("users/{username}/feishu-bot")]
    public async Task<ActionResult<UserFeishuBotConfigDto>> GetFeishuBotConfig(string username)
    {
        var config = await _userFeishuBotConfigService.GetByUsernameAsync(username);
        if (config == null)
        {
            return Ok(new UserFeishuBotConfigDto { Username = username, IsEnabled = false });
        }

        return Ok(MapFeishuConfig(config));
    }

    [HttpPut("users/{username}/feishu-bot")]
    public async Task<ActionResult> SaveFeishuBotConfig(string username, [FromBody] UserFeishuBotConfigDto request)
    {
        var success = await _userFeishuBotConfigService.SaveAsync(new UserFeishuBotConfigEntity
        {
            Username = username.Trim(),
            IsEnabled = request.IsEnabled,
            AppId = request.AppId,
            AppSecret = request.AppSecret,
            EncryptKey = request.EncryptKey,
            VerificationToken = request.VerificationToken,
            DefaultCardTitle = request.DefaultCardTitle,
            ThinkingMessage = request.ThinkingMessage,
            HttpTimeoutSeconds = request.HttpTimeoutSeconds,
            StreamingThrottleMs = request.StreamingThrottleMs
        });

        return Ok(new { success });
    }

    [HttpDelete("users/{username}/feishu-bot")]
    public async Task<ActionResult> DeleteFeishuBotConfig(string username)
    {
        var success = await _userFeishuBotConfigService.DeleteAsync(username);
        return Ok(new { success });
    }

    private static UserAccountResponseDto MapUser(UserAccountEntity account)
    {
        return new UserAccountResponseDto
        {
            Username = account.Username,
            DisplayName = account.DisplayName,
            Role = account.Role,
            Status = account.Status,
            LastLoginAt = account.LastLoginAt,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt
        };
    }

    private static UserFeishuBotConfigDto MapFeishuConfig(UserFeishuBotConfigEntity config)
    {
        return new UserFeishuBotConfigDto
        {
            Username = config.Username,
            IsEnabled = config.IsEnabled,
            AppId = config.AppId,
            AppSecret = config.AppSecret,
            EncryptKey = config.EncryptKey,
            VerificationToken = config.VerificationToken,
            DefaultCardTitle = config.DefaultCardTitle,
            ThinkingMessage = config.ThinkingMessage,
            HttpTimeoutSeconds = config.HttpTimeoutSeconds,
            StreamingThrottleMs = config.StreamingThrottleMs
        };
    }
}

public sealed class SaveUserRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Password { get; set; }
    public string Role { get; set; } = UserAccessConstants.UserRole;
    public string Status { get; set; } = UserAccessConstants.EnabledStatus;
}

public sealed class UserAccountResponseDto
{
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = UserAccessConstants.UserRole;
    public string Status { get; set; } = UserAccessConstants.EnabledStatus;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class UpdateUserStatusRequestDto
{
    public bool Enabled { get; set; }
}

public sealed class UpdateUserToolsRequestDto
{
    public List<string>? AllowedToolIds { get; set; }
}

public sealed class UpdateWorkspacePoliciesRequestDto
{
    public List<string>? AllowedDirectories { get; set; }
}

public sealed class UserFeishuBotConfigDto
{
    public string? Username { get; set; }
    public bool IsEnabled { get; set; }
    public string? AppId { get; set; }
    public string? AppSecret { get; set; }
    public string? EncryptKey { get; set; }
    public string? VerificationToken { get; set; }
    public string? DefaultCardTitle { get; set; }
    public string? ThinkingMessage { get; set; }
    public int? HttpTimeoutSeconds { get; set; }
    public int? StreamingThrottleMs { get; set; }
}
