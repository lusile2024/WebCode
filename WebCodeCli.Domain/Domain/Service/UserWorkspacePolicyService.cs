using Microsoft.Extensions.DependencyInjection;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.UserWorkspacePolicy;

namespace WebCodeCli.Domain.Domain.Service;

[ServiceDescription(typeof(IUserWorkspacePolicyService), ServiceLifetime.Scoped)]
public class UserWorkspacePolicyService : IUserWorkspacePolicyService
{
    private readonly IUserWorkspacePolicyRepository _repository;

    public UserWorkspacePolicyService(IUserWorkspacePolicyRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<string>> GetAllowedDirectoriesAsync(string username)
    {
        var policies = await _repository.GetByUsernameAsync(username.Trim());
        return policies.Select(x => x.DirectoryPath).ToList();
    }

    public async Task<bool> IsPathAllowedAsync(string username, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        var normalizedTarget = NormalizePath(directoryPath);
        var policies = await _repository.GetByUsernameAsync(username.Trim());
        if (!policies.Any())
        {
            return true;
        }

        return policies
            .Select(x => NormalizePath(x.DirectoryPath))
            .Any(root => normalizedTarget.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> SaveAllowedDirectoriesAsync(string username, IEnumerable<string> allowedDirectories)
    {
        var normalizedUsername = username.Trim();
        var normalizedDirectories = allowedDirectories
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _repository.DeleteByUsernameAsync(normalizedUsername);
        if (!normalizedDirectories.Any())
        {
            return true;
        }

        var now = DateTime.Now;
        var entities = normalizedDirectories.Select(path => new UserWorkspacePolicyEntity
        {
            Username = normalizedUsername,
            DirectoryPath = path,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        return await _repository.InsertRangeAsync(entities);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
