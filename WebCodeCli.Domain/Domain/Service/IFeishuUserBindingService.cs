namespace WebCodeCli.Domain.Domain.Service;

public interface IFeishuUserBindingService
{
    Task<string?> GetBoundWebUsernameAsync(string feishuUserId);
    Task<bool> IsBoundAsync(string feishuUserId);
    Task<(bool Success, string? ErrorMessage, string? WebUsername)> BindAsync(string feishuUserId, string webUsername, string? appId = null);
    Task<bool> UnbindAsync(string feishuUserId);
    Task<List<string>> GetBindableWebUsernamesAsync(string? appId = null);
    Task<HashSet<string>> GetAllBoundWebUsernamesAsync();
}
