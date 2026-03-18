using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Repositories.Base.CliToolEnv;
using WebCodeCli.Domain.Repositories.Base.UserCliToolEnv;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// CLI 工具环境变量服务接口
/// </summary>
public interface ICliToolEnvironmentService
{
    /// <summary>
    /// 获取指定工具的环境变量配置（优先从数据库读取,否则从appsettings读取）
    /// </summary>
    Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string toolId, string? username = null);

    /// <summary>
    /// 保存指定工具的环境变量配置到数据库
    /// </summary>
    Task<bool> SaveEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars, string? username = null);

    /// <summary>
    /// 删除指定工具的环境变量配置
    /// </summary>
    Task<bool> DeleteEnvironmentVariablesAsync(string toolId, string? username = null);

    /// <summary>
    /// 重置为appsettings中的默认配置
    /// </summary>
    Task<Dictionary<string, string>> ResetToDefaultAsync(string toolId, string? username = null);
}

/// <summary>
/// CLI 工具环境变量服务实现
/// </summary>
[ServiceDescription(typeof(ICliToolEnvironmentService), ServiceLifetime.Scoped)]
public class CliToolEnvironmentService : ICliToolEnvironmentService
{
    private readonly ILogger<CliToolEnvironmentService> _logger;
    private readonly CliToolsOption _options;
    private readonly ICliToolEnvironmentVariableRepository _repository;
    private readonly IUserCliToolEnvironmentVariableRepository _userRepository;
    private readonly IUserContextService _userContextService;

    public CliToolEnvironmentService(
        ILogger<CliToolEnvironmentService> logger,
        IOptions<CliToolsOption> options,
        ICliToolEnvironmentVariableRepository repository,
        IUserCliToolEnvironmentVariableRepository userRepository,
        IUserContextService userContextService)
    {
        _logger = logger;
        _options = options.Value;
        _repository = repository;
        _userRepository = userRepository;
        _userContextService = userContextService;
    }

    /// <summary>
    /// 获取指定工具的环境变量配置（优先从数据库读取,否则从appsettings读取）
    /// </summary>
    public async Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string toolId, string? username = null)
    {
        try
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tool = _options.Tools.FirstOrDefault(t => t.Id == toolId);
            if (tool?.EnvironmentVariables != null)
            {
                foreach (var kvp in tool.EnvironmentVariables.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)))
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            var dbEnvVars = await _repository.GetEnvironmentVariablesByToolIdAsync(toolId);
            foreach (var kvp in dbEnvVars.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)))
            {
                result[kvp.Key] = kvp.Value;
            }

            var resolvedUsername = ResolveUsername(username);
            if (!string.IsNullOrWhiteSpace(resolvedUsername))
            {
                var userEnvVars = await _userRepository.GetEnvironmentVariablesAsync(resolvedUsername, toolId);
                foreach (var kvp in userEnvVars.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)))
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            _logger.LogInformation("获取工具 {ToolId} 的环境变量配置，用户={Username}，最终 {Count} 个", toolId, resolvedUsername, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取工具 {ToolId} 的环境变量失败", toolId);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 保存指定工具的环境变量配置到数据库
    /// </summary>
    public async Task<bool> SaveEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars, string? username = null)
    {
        try
        {
            var resolvedUsername = ResolveUsername(username);
            if (string.IsNullOrWhiteSpace(resolvedUsername))
            {
                return false;
            }

            var result = await _userRepository.SaveEnvironmentVariablesAsync(resolvedUsername, toolId, envVars);
            if (result)
            {
                _logger.LogInformation("成功保存工具 {ToolId} 的用户环境变量配置，用户={Username}", toolId, resolvedUsername);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存工具 {ToolId} 的环境变量失败", toolId);
            return false;
        }
    }

    /// <summary>
    /// 删除指定工具的环境变量配置
    /// </summary>
    public async Task<bool> DeleteEnvironmentVariablesAsync(string toolId, string? username = null)
    {
        try
        {
            var resolvedUsername = ResolveUsername(username);
            if (string.IsNullOrWhiteSpace(resolvedUsername))
            {
                return false;
            }

            var result = await _userRepository.DeleteByToolIdAsync(resolvedUsername, toolId);
            if (result)
            {
                _logger.LogInformation("成功删除工具 {ToolId} 的用户环境变量配置，用户={Username}", toolId, resolvedUsername);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除工具 {ToolId} 的环境变量失败", toolId);
            return false;
        }
    }

    /// <summary>
    /// 重置为appsettings中的默认配置
    /// </summary>
    public async Task<Dictionary<string, string>> ResetToDefaultAsync(string toolId, string? username = null)
    {
        try
        {
            await DeleteEnvironmentVariablesAsync(toolId, username);
            return await GetEnvironmentVariablesAsync(toolId, username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重置工具 {ToolId} 的环境变量失败", toolId);
            return new Dictionary<string, string>();
        }
    }

    private string ResolveUsername(string? username)
    {
        return string.IsNullOrWhiteSpace(username)
            ? _userContextService.GetCurrentUsername()
            : username.Trim();
    }
}
