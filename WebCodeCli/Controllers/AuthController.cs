using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.UserAccount;

namespace WebCodeCli.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly WebCodeCli.Domain.Domain.Service.IAuthenticationService _authenticationService;
    private readonly IUserAccountService _userAccountService;

    public AuthController(
        WebCodeCli.Domain.Domain.Service.IAuthenticationService authenticationService,
        IUserAccountService userAccountService)
    {
        _authenticationService = authenticationService;
        _userAccountService = userAccountService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        if (!_authenticationService.IsAuthenticationEnabled())
        {
            return Ok(new AuthStateDto
            {
                Success = true,
                IsAuthenticated = true,
                Username = "default",
                DisplayName = "default",
                Role = UserAccessConstants.UserRole
            });
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new AuthStateDto
            {
                Success = false,
                ErrorMessage = "用户名和密码不能为空。"
            });
        }

        var normalizedUsername = request.Username.Trim();
        var existingAccount = await _userAccountService.GetByUsernameAsync(normalizedUsername);
        if (existingAccount != null &&
            string.Equals(existingAccount.Status, UserAccessConstants.DisabledStatus, StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(new AuthStateDto
            {
                Success = false,
                ErrorMessage = "用户已被禁用。"
            });
        }

        var account = await _userAccountService.ValidateCredentialsAsync(normalizedUsername, request.Password);
        if (account == null)
        {
            return Unauthorized(new AuthStateDto
            {
                Success = false,
                ErrorMessage = "用户名或密码错误。"
            });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, account.Username),
            new(ClaimTypes.Role, account.Role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });

        await _userAccountService.UpdateLastLoginAsync(account.Username, DateTime.Now);

        return Ok(new AuthStateDto
        {
            Success = true,
            IsAuthenticated = true,
            Username = account.Username,
            DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
            Role = account.Role
        });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new AuthStateDto
        {
            Success = true,
            IsAuthenticated = false
        });
    }

    [HttpGet("me")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCurrentUser()
    {
        if (!_authenticationService.IsAuthenticationEnabled())
        {
            return Ok(new AuthStateDto
            {
                Success = true,
                IsAuthenticated = false
            });
        }

        if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(User.Identity.Name))
        {
            return Ok(new AuthStateDto
            {
                Success = true,
                IsAuthenticated = false
            });
        }

        var account = await _userAccountService.GetByUsernameAsync(User.Identity.Name);
        if (account == null ||
            string.Equals(account.Status, UserAccessConstants.DisabledStatus, StringComparison.OrdinalIgnoreCase))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new AuthStateDto
            {
                Success = true,
                IsAuthenticated = false
            });
        }

        return Ok(new AuthStateDto
        {
            Success = true,
            IsAuthenticated = true,
            Username = account.Username,
            DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
            Role = account.Role
        });
    }
}

public sealed class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class AuthStateDto
{
    public bool Success { get; set; }
    public bool IsAuthenticated { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
    public string? ErrorMessage { get; set; }
}
