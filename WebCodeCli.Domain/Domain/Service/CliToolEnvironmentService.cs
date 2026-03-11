using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.CliToolEnv;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// CLI 工具环境变量服务接口
/// </summary>
public interface ICliToolEnvironmentService
{
    /// <summary>
    /// 获取指定工具的环境变量配置（优先使用激活方案，其次数据库默认配置，最后 appsettings）
    /// </summary>
    Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string toolId);

    /// <summary>
    /// 保存指定工具的默认环境变量配置到数据库
    /// </summary>
    Task<bool> SaveEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars);

    /// <summary>
    /// 删除指定工具的环境变量配置
    /// </summary>
    Task<bool> DeleteEnvironmentVariablesAsync(string toolId);

    /// <summary>
    /// 重置为appsettings中的默认配置
    /// </summary>
    Task<Dictionary<string, string>> ResetToDefaultAsync(string toolId);

    // ── 配置方案（多套 AI 环境变量） ──

    /// <summary>
    /// 获取指定工具的所有配置方案
    /// </summary>
    Task<List<CliToolEnvProfile>> GetProfilesAsync(string toolId);

    /// <summary>
    /// 保存（新建或更新）一个配置方案
    /// </summary>
    Task<CliToolEnvProfile?> SaveProfileAsync(string toolId, int profileId, string profileName, Dictionary<string, string> envVars);

    /// <summary>
    /// 激活指定配置方案（将其设为当前生效方案）
    /// </summary>
    Task<bool> ActivateProfileAsync(string toolId, int profileId);

    /// <summary>
    /// 取消所有方案激活，回退到默认配置
    /// </summary>
    Task<bool> DeactivateProfilesAsync(string toolId);

    /// <summary>
    /// 删除指定配置方案
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

    public CliToolEnvironmentService(
        ILogger<CliToolEnvironmentService> logger,
        IOptions<CliToolsOption> options,
        ICliToolEnvironmentVariableRepository repository,
        ICliToolEnvProfileRepository profileRepository)
    {
        _logger = logger;
        _options = options.Value;
        _repository = repository;
        _profileRepository = profileRepository;
    }

    /// <summary>
    /// 获取指定工具的环境变量配置
    /// 优先级：激活的配置方案 > 数据库默认配置 > appsettings 配置
    /// </summary>
    public async Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string toolId)
    {
        try
        {
            // 1. 优先使用激活的配置方案
            var activeProfile = await _profileRepository.GetActiveProfileAsync(toolId);
            if (activeProfile != null && !string.IsNullOrWhiteSpace(activeProfile.EnvVarsJson))
            {
                try
                {
                    _logger.LogInformation("从激活方案 [{ProfileName}] 加载工具 {ToolId} 的环境变量配置", activeProfile.ProfileName, toolId);
                    var profileVars = JsonSerializer.Deserialize<Dictionary<string, string>>(activeProfile.EnvVarsJson) ?? new();
                    return profileVars
                        .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogWarning(jsonEx,
                        "激活方案 [{ProfileName}] 的环境变量 JSON 无效，忽略该方案并回退到数据库和配置文件。ToolId: {ToolId}",
                        activeProfile.ProfileName,
                        toolId);
                }
            }

            // 2. 从数据库默认配置读取
            var dbEnvVars = await _repository.GetEnvironmentVariablesByToolIdAsync(toolId);
            if (dbEnvVars.Any())
            {
                _logger.LogInformation("从数据库加载工具 {ToolId} 的环境变量配置", toolId);
                return dbEnvVars
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            // 3. 从 appsettings 读取
            var tool = _options.Tools.FirstOrDefault(t => t.Id == toolId);
            if (tool?.EnvironmentVariables != null && tool.EnvironmentVariables.Any())
            {
                _logger.LogInformation("从配置文件加载工具 {ToolId} 的环境变量配置", toolId);
                return tool.EnvironmentVariables
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            return new Dictionary<string, string>();
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
    public async Task<bool> SaveEnvironmentVariablesAsync(string toolId, Dictionary<string, string> envVars)
    {
        try
        {
            var result = await _repository.SaveEnvironmentVariablesAsync(toolId, envVars);
            if (result)
            {
                _logger.LogInformation("成功保存工具 {ToolId} 的环境变量配置", toolId);
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
    public async Task<bool> DeleteEnvironmentVariablesAsync(string toolId)
    {
        try
        {
            var result = await _repository.DeleteByToolIdAsync(toolId);
            if (result)
            {
                _logger.LogInformation("成功删除工具 {ToolId} 的环境变量配置", toolId);
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
    public async Task<Dictionary<string, string>> ResetToDefaultAsync(string toolId)
    {
        try
        {
            // 删除数据库配置
            await _repository.DeleteByToolIdAsync(toolId);

            // 返回appsettings中的配置
            var tool = _options.Tools.FirstOrDefault(t => t.Id == toolId);
            if (tool?.EnvironmentVariables != null && tool.EnvironmentVariables.Any())
            {
                _logger.LogInformation("重置工具 {ToolId} 的环境变量为默认配置", toolId);
                return new Dictionary<string, string>(tool.EnvironmentVariables);
            }

            return new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重置工具 {ToolId} 的环境变量失败", toolId);
            return new Dictionary<string, string>();
        }
    }

    // ── 配置方案（多套 AI 环境变量） ──

    /// <summary>
    /// 获取指定工具的所有配置方案
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
    /// 保存（新建或更新）一个配置方案
    /// </summary>
    public async Task<CliToolEnvProfile?> SaveProfileAsync(string toolId, int profileId, string profileName, Dictionary<string, string> envVars)
    {
        try
        {
            var envVarsJson = JsonSerializer.Serialize(envVars);

            if (profileId <= 0)
            {
                // 新建方案
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
            else
            {
                // 更新已有方案
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存工具 {ToolId} 的配置方案失败", toolId);
            return null;
        }
    }

    /// <summary>
    /// 激活指定配置方案
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
    /// 取消所有方案激活，回退到默认配置
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
    /// 删除指定配置方案
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
}
