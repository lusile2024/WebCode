using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.CliToolEnv;
using WebCodeCli.Domain.Repositories.Base.UserCliToolEnv;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// CLI 工具环境变量服务接口
/// </summary>
public interface ICliToolEnvironmentService
{
    /// <summary>
    /// 获取指定工具的环境变量配置。
    /// 优先级：激活方案 > 数据库默认配置 > appsettings，然后叠加当前用户覆盖。
    /// </summary>
    Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string toolId, string? username = null);

    /// <summary>
    /// 保存指定工具的用户环境变量配置到数据库。
    /// </summary>
    Task<bool> SaveEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars, string? username = null);

    /// <summary>
    /// 删除指定工具的用户环境变量配置。
    /// </summary>
    Task<bool> DeleteEnvironmentVariablesAsync(string toolId, string? username = null);

    /// <summary>
    /// 重置为继承默认配置。
    /// </summary>
    Task<Dictionary<string, string>> ResetToDefaultAsync(string toolId, string? username = null);

    /// <summary>
    /// 获取指定工具的所有配置方案。
    /// </summary>
    Task<List<CliToolEnvProfile>> GetProfilesAsync(string toolId);

    /// <summary>
    /// 保存（新建或更新）一个配置方案。
    /// </summary>
    Task<CliToolEnvProfile?> SaveProfileAsync(string toolId, int profileId, string profileName, Dictionary<string, string> envVars);

    /// <summary>
    /// 激活指定配置方案（将其设为当前生效方案）。
    /// </summary>
    Task<bool> ActivateProfileAsync(string toolId, int profileId);

    /// <summary>
    /// 取消所有方案激活，回退到默认配置。
    /// </summary>
    Task<bool> DeactivateProfilesAsync(string toolId);

    /// <summary>
    /// 删除指定配置方案。
    /// </summary>
    Task<bool> DeleteProfileAsync(string toolId, int profileId);
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
    private readonly ICliToolEnvProfileRepository _profileRepository;
    private readonly IUserCliToolEnvironmentVariableRepository _userRepository;
    private readonly IUserContextService _userContextService;

    public CliToolEnvironmentService(
        ILogger<CliToolEnvironmentService> logger,
        IOptions<CliToolsOption> options,
        ICliToolEnvironmentVariableRepository repository,
        ICliToolEnvProfileRepository profileRepository,
        IUserCliToolEnvironmentVariableRepository userRepository,
        IUserContextService userContextService)
    {
        _logger = logger;
        _options = options.Value;
        _repository = repository;
        _profileRepository = profileRepository;
        _userRepository = userRepository;
        _userContextService = userContextService;
    }

    /// <summary>
    /// 获取指定工具的环境变量配置。
    /// 优先级：激活方案 > 数据库默认配置 > appsettings，然后叠加当前用户覆盖。
    /// </summary>
    public async Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string toolId, string? username = null)
    {
        try
        {
            var result = await GetInheritedEnvironmentVariablesAsync(toolId);

            var resolvedUsername = ResolveUsername(username);
            if (!string.IsNullOrWhiteSpace(resolvedUsername))
            {
                var userEnvVars = await _userRepository.GetEnvironmentVariablesAsync(resolvedUsername, toolId);
                ApplyOverrides(result, userEnvVars);
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
    /// 保存指定工具的用户环境变量配置到数据库。
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

            var normalizedEnvVars = NormalizeEnvVars(envVars, keepEmptyValues: false);
            var inheritedEnvVars = await GetInheritedEnvironmentVariablesAsync(toolId);
            var persistedEnvVars = BuildPersistedUserEnvVars(normalizedEnvVars, inheritedEnvVars);

            var result = await _userRepository.SaveEnvironmentVariablesAsync(resolvedUsername, toolId, persistedEnvVars);
            if (result)
            {
                _logger.LogInformation(
                    "成功保存工具 {ToolId} 的用户环境变量配置，用户={Username}，提交 {SubmittedCount} 个，落库 {PersistedCount} 个",
                    toolId,
                    resolvedUsername,
                    normalizedEnvVars.Count,
                    persistedEnvVars.Count);
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
    /// 删除指定工具的用户环境变量配置。
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
    /// 重置为继承默认配置。
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

    /// <summary>
    /// 获取指定工具的所有配置方案。
    /// </summary>
    public async Task<List<CliToolEnvProfile>> GetProfilesAsync(string toolId)
    {
        try
        {
            return await _profileRepository.GetProfilesByToolIdAsync(toolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取工具 {ToolId} 的配置方案列表失败", toolId);
            return new List<CliToolEnvProfile>();
        }
    }

    /// <summary>
    /// 保存（新建或更新）一个配置方案。
    /// </summary>
    public async Task<CliToolEnvProfile?> SaveProfileAsync(string toolId, int profileId, string profileName, Dictionary<string, string> envVars)
    {
        try
        {
            var envVarsJson = JsonSerializer.Serialize(envVars);

            if (profileId <= 0)
            {
                var newProfile = new CliToolEnvProfile
                {
                    ToolId = toolId,
                    ProfileName = profileName,
                    IsActive = false,
                    EnvVarsJson = envVarsJson,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                var newId = await _profileRepository.InsertReturnIdentityAsync(newProfile);
                newProfile.Id = newId;
                _logger.LogInformation("成功新建工具 {ToolId} 的配置方案 [{ProfileName}]", toolId, profileName);
                return newProfile;
            }

            var existing = await _profileRepository.GetByIdAsync(profileId);
            if (existing == null || existing.ToolId != toolId)
            {
                _logger.LogWarning("未找到工具 {ToolId} 的配置方案 {ProfileId}", toolId, profileId);
                return null;
            }

            existing.ProfileName = profileName;
            existing.EnvVarsJson = envVarsJson;
            existing.UpdatedAt = DateTime.Now;
            await _profileRepository.UpdateAsync(existing);
            _logger.LogInformation("成功更新工具 {ToolId} 的配置方案 [{ProfileName}]", toolId, profileName);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存工具 {ToolId} 的配置方案失败", toolId);
            return null;
        }
    }

    /// <summary>
    /// 激活指定配置方案。
    /// </summary>
    public async Task<bool> ActivateProfileAsync(string toolId, int profileId)
    {
        try
        {
            var result = await _profileRepository.ActivateProfileAsync(toolId, profileId);
            if (result)
            {
                _logger.LogInformation("成功激活工具 {ToolId} 的配置方案 {ProfileId}", toolId, profileId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "激活工具 {ToolId} 的配置方案 {ProfileId} 失败", toolId, profileId);
            return false;
        }
    }

    /// <summary>
    /// 取消所有方案激活，回退到默认配置。
    /// </summary>
    public async Task<bool> DeactivateProfilesAsync(string toolId)
    {
        try
        {
            var result = await _profileRepository.DeactivateAllProfilesAsync(toolId);
            if (result)
            {
                _logger.LogInformation("已取消工具 {ToolId} 的所有方案激活状态", toolId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消工具 {ToolId} 方案激活状态失败", toolId);
            return false;
        }
    }

    /// <summary>
    /// 删除指定配置方案。
    /// </summary>
    public async Task<bool> DeleteProfileAsync(string toolId, int profileId)
    {
        try
        {
            var result = await _profileRepository.DeleteProfileAsync(toolId, profileId);
            if (result)
            {
                _logger.LogInformation("成功删除工具 {ToolId} 的配置方案 {ProfileId}", toolId, profileId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除工具 {ToolId} 的配置方案 {ProfileId} 失败", toolId, profileId);
            return false;
        }
    }

    private string ResolveUsername(string? username)
    {
        return string.IsNullOrWhiteSpace(username)
            ? _userContextService.GetCurrentUsername()
            : username.Trim();
    }

    private async Task<Dictionary<string, string>> GetInheritedEnvironmentVariablesAsync(string toolId)
    {
        var activeProfile = await _profileRepository.GetActiveProfileAsync(toolId);
        if (activeProfile != null && !string.IsNullOrWhiteSpace(activeProfile.EnvVarsJson))
        {
            try
            {
                _logger.LogInformation("从激活方案 [{ProfileName}] 加载工具 {ToolId} 的环境变量配置", activeProfile.ProfileName, toolId);
                var profileVars = JsonSerializer.Deserialize<Dictionary<string, string>>(activeProfile.EnvVarsJson) ?? new();
                return NormalizeEnvVars(profileVars, keepEmptyValues: false);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(
                    jsonEx,
                    "激活方案 [{ProfileName}] 的环境变量 JSON 无效，忽略该方案并回退到数据库和配置文件。ToolId: {ToolId}",
                    activeProfile.ProfileName,
                    toolId);
            }
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tool = _options.Tools.FirstOrDefault(t => t.Id == toolId);
        if (tool?.EnvironmentVariables != null)
        {
            ApplyOverrides(result, tool.EnvironmentVariables);
        }

        var dbEnvVars = await _repository.GetEnvironmentVariablesByToolIdAsync(toolId);
        ApplyOverrides(result, dbEnvVars);
        return result;
    }

    private static Dictionary<string, string> NormalizeEnvVars(Dictionary<string, string> envVars, bool keepEmptyValues)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in envVars)
        {
            var key = kvp.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = kvp.Value?.Trim() ?? string.Empty;
            if (!keepEmptyValues && string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static Dictionary<string, string> BuildPersistedUserEnvVars(
        Dictionary<string, string> requestedEnvVars,
        Dictionary<string, string> inheritedEnvVars)
    {
        var result = new Dictionary<string, string>(requestedEnvVars, StringComparer.OrdinalIgnoreCase);
        foreach (var inheritedKey in inheritedEnvVars.Keys)
        {
            if (!requestedEnvVars.ContainsKey(inheritedKey))
            {
                // 空字符串表示显式移除继承的环境变量，避免默认值在下次读取时再次合并回来。
                result[inheritedKey] = string.Empty;
            }
        }

        return result;
    }

    private static void ApplyOverrides(Dictionary<string, string> target, IEnumerable<KeyValuePair<string, string>> envVars)
    {
        foreach (var kvp in envVars)
        {
            var key = kvp.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = kvp.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                target.Remove(key);
                continue;
            }

            target[key] = value;
        }
    }
}
