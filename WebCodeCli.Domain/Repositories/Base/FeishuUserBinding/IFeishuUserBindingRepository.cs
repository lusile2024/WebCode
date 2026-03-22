using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.FeishuUserBinding;

public interface IFeishuUserBindingRepository : IRepository<FeishuUserBindingEntity>
{
    Task<FeishuUserBindingEntity?> GetByFeishuUserIdAsync(string feishuUserId);
    Task<List<FeishuUserBindingEntity>> GetByWebUsernameAsync(string webUsername);
}
