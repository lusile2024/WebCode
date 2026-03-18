namespace WebCodeCli.Domain.Domain.Service;

public interface IUserToolPolicyService
{
    Task<bool> IsToolAllowedAsync(string username, string toolId);
    Task<HashSet<string>> GetAllowedToolIdsAsync(string username, IEnumerable<string> allToolIds);
    Task<Dictionary<string, bool>> GetPolicyMapAsync(string username, IEnumerable<string> allToolIds);
    Task<bool> SaveAllowedToolsAsync(string username, IEnumerable<string> allowedToolIds, IEnumerable<string> allToolIds);
}
