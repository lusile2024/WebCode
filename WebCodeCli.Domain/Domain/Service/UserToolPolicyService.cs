using Microsoft.Extensions.DependencyInjection;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.UserToolPolicy;

namespace WebCodeCli.Domain.Domain.Service;

[ServiceDescription(typeof(IUserToolPolicyService), ServiceLifetime.Scoped)]
public class UserToolPolicyService : IUserToolPolicyService
{
    private readonly IUserToolPolicyRepository _repository;

    public UserToolPolicyService(IUserToolPolicyRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> IsToolAllowedAsync(string username, string toolId)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(toolId))
        {
            return false;
        }

        var policies = await _repository.GetByUsernameAsync(username.Trim());
        if (!policies.Any())
        {
            return true;
        }

        var matchedPolicy = policies.FirstOrDefault(x => string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase));
        return matchedPolicy?.IsAllowed ?? true;
    }

    public async Task<HashSet<string>> GetAllowedToolIdsAsync(string username, IEnumerable<string> allToolIds)
    {
        var policyMap = await GetPolicyMapAsync(username, allToolIds);
        return policyMap
            .Where(x => x.Value)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, bool>> GetPolicyMapAsync(string username, IEnumerable<string> allToolIds)
    {
        var toolIds = allToolIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var policies = await _repository.GetByUsernameAsync(username.Trim());
        if (!policies.Any())
        {
            return toolIds.ToDictionary(x => x, _ => true, StringComparer.OrdinalIgnoreCase);
        }

        return toolIds.ToDictionary(
            toolId => toolId,
            toolId => policies.FirstOrDefault(x => string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase))?.IsAllowed ?? true,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> SaveAllowedToolsAsync(string username, IEnumerable<string> allowedToolIds, IEnumerable<string> allToolIds)
    {
        var normalizedUsername = username.Trim();
        var allowedSet = allowedToolIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allToolSet = allToolIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _repository.DeleteByUsernameAsync(normalizedUsername);
        if (!allToolSet.Any())
        {
            return true;
        }

        var now = DateTime.Now;
        var entities = allToolSet.Select(toolId => new UserToolPolicyEntity
        {
            Username = normalizedUsername,
            ToolId = toolId,
            IsAllowed = allowedSet.Contains(toolId),
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        return await _repository.InsertRangeAsync(entities);
    }
}
