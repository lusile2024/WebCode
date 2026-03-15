using AntSK.Domain.Repositories.Base;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Repositories.Base.FeishuUserBinding;

[ServiceDescription(typeof(IFeishuUserBindingRepository), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
public class FeishuUserBindingRepository : Repository<FeishuUserBindingEntity>, IFeishuUserBindingRepository
{
    public async Task<FeishuUserBindingEntity?> GetByFeishuUserIdAsync(string feishuUserId)
    {
        return await GetFirstAsync(x => x.FeishuUserId == feishuUserId);
    }

    public async Task<List<FeishuUserBindingEntity>> GetByWebUsernameAsync(string webUsername)
    {
        return await GetListAsync(x => x.WebUsername == webUsername);
    }
}
