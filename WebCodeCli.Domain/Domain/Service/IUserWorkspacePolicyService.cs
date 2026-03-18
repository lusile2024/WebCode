namespace WebCodeCli.Domain.Domain.Service;

public interface IUserWorkspacePolicyService
{
    Task<List<string>> GetAllowedDirectoriesAsync(string username);
    Task<bool> IsPathAllowedAsync(string username, string directoryPath);
    Task<bool> SaveAllowedDirectoriesAsync(string username, IEnumerable<string> allowedDirectories);
}
