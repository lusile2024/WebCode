using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Repositories.Base;
using AntSK.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.CliToolEnv;

/// <summary>
/// CLI 工具环境变量配置方案仓储实现
/// </summary>
[ServiceDescription(typeof(ICliToolEnvProfileRepository), ServiceLifetime.Scoped)]
public class CliToolEnvProfileRepository : Repository<CliToolEnvProfile>, ICliToolEnvProfileRepository
{
    private readonly ILogger<CliToolEnvProfileRepository> _logger;

    public CliToolEnvProfileRepository(ILogger<CliToolEnvProfileRepository> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取指定工具的所有配置方案
    /// </summary>
    public async Task<List<CliToolEnvProfile>> GetProfilesByToolIdAsync(string toolId)
    {
        try
        {
            return await GetListAsync(x => x.ToolId == toolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取工具 {ToolId} 的配置方案列表失败", toolId);
            return new List<CliToolEnvProfile>();
        }
    }

    /// <summary>
    /// 获取指定工具的当前激活方案
    /// </summary>
    public async Task<CliToolEnvProfile?> GetActiveProfileAsync(string toolId)
    {
        try
        {
            return await GetFirstAsync(x => x.ToolId == toolId && x.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取工具 {ToolId} 的激活方案失败", toolId);
            return null;
        }
    }

    /// <summary>
    /// 激活指定方案（同时取消同工具其他方案的激活状态）
    /// </summary>
    public async Task<bool> ActivateProfileAsync(string toolId, int profileId)
    {
        try
        {
            var profiles = await GetListAsync(x => x.ToolId == toolId);
            if (!profiles.Any())
            {
                return false;
            }

            var targetProfile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (targetProfile == null)
            {
                return false;
            }

            await GetDB().Ado.BeginTranAsync();
            try
            {
                foreach (var profile in profiles)
                {
                    profile.IsActive = profile.Id == profileId;
                    profile.UpdatedAt = DateTime.Now;
                }

                await UpdateRangeAsync(profiles);
                await GetDB().Ado.CommitTranAsync();
            }
            catch (Exception)
            {
                await GetDB().Ado.RollbackTranAsync();
                throw;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "激活工具 {ToolId} 的方案 {ProfileId} 失败", toolId, profileId);
            return false;
        }
    }

    /// <summary>
    /// 取消指定工具的所有方案激活状态
    /// </summary>
    public async Task<bool> DeactivateAllProfilesAsync(string toolId)
    {
        try
        {
            var profiles = await GetListAsync(x => x.ToolId == toolId && x.IsActive);
            foreach (var profile in profiles)
            {
                profile.IsActive = false;
                profile.UpdatedAt = DateTime.Now;
            }
            if (profiles.Any())
            {
                await UpdateRangeAsync(profiles);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消工具 {ToolId} 所有方案激活状态失败", toolId);
            return false;
        }
    }

    /// <summary>
    /// 删除指定方案
    /// </summary>
    public async Task<bool> DeleteProfileAsync(string toolId, int profileId)
    {
        try
        {
            return await DeleteAsync(x => x.Id == profileId && x.ToolId == toolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除工具 {ToolId} 的方案 {ProfileId} 失败", toolId, profileId);
            return false;
        }
    }
}
