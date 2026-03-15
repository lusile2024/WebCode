namespace WebCodeCli.Domain.Domain.Service;

public interface IFeishuUserBindingService
{
    Task<string?> GetBoundWebUsernameAsync(string feishuUserId);
    Task<bool> IsBoundAsync(string feishuUserId);
    Task<(bool Success, string? ErrorMessage, string? WebUsername)> BindAsync(string feishuUserId, string webUsername);
    Task<bool> UnbindAsync(string feishuUserId);
    Task<List<string>> GetBindableWebUsernamesAsync();
    Task<HashSet<string>> GetAllBoundWebUsernamesAsync();
}
