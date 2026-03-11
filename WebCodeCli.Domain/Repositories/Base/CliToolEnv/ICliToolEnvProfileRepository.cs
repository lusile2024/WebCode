using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.CliToolEnv;

/// <summary>
/// CLI 工具环境变量配置方案仓储接口
/// </summary>
public interface ICliToolEnvProfileRepository : IRepository<CliToolEnvProfile>
{
    /// <summary>
    /// 获取指定工具的所有配置方案
    /// </summary>
    Task<List<CliToolEnvProfile>> GetProfilesByToolIdAsync(string toolId);

    /// <summary>
    /// 获取指定工具的当前激活方案
    /// </summary>
    Task<CliToolEnvProfile?> GetActiveProfileAsync(string toolId);

    /// <summary>
    /// 激活指定方案（同时取消同工具其他方案的激活状态）
    /// </summary>
    Task<bool> ActivateProfileAsync(string toolId, int profileId);

    /// <summary>
    /// 取消指定工具的所有方案激活状态
    /// </summary>
    Task<bool> DeactivateAllProfilesAsync(string toolId);

    /// <summary>
    /// 删除指定工具的指定方案
    /// </summary>
    Task<bool> DeleteProfileAsync(string toolId, int profileId);
}
