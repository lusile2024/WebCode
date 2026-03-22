using Microsoft.Extensions.Configuration;
using System.Reflection;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.ChatSession;
using WebCodeCli.Domain.Repositories.Base.Project;

namespace WebCodeCli.Domain.Tests;

public class SessionDirectoryServiceTests
{
    [Fact]
    public async Task BrowseAllowedDirectoriesAsync_SkipsReservedWindowsDeviceEntries()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var service = CreateService(repoRoot);

        var result = await service.BrowseAllowedDirectoriesAsync(repoRoot);

        Assert.Equal(repoRoot, result.CurrentPath);
        Assert.DoesNotContain(result.Entries, entry => string.Equals(entry.Name, "nul", StringComparison.OrdinalIgnoreCase));
    }

    private static SessionDirectoryService CreateService(string allowedRoot)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:AllowedRoots:0"] = allowedRoot
            })
            .Build();

        return new SessionDirectoryService(
            CreateProxy<IChatSessionRepository>(),
            new StubWorkspaceRegistryService(),
            CreateProxy<IWorkspaceAuthorizationService>(),
            CreateProxy<IProjectRepository>(),
            new StubUserWorkspacePolicyService(),
            configuration);
    }

    private static T CreateProxy<T>() where T : class
    {
        return DispatchProxy.Create<T, DefaultInterfaceProxy<T>>();
    }

    private sealed class StubWorkspaceRegistryService : IWorkspaceRegistryService
    {
        public string NormalizePath(string path) => path;

        public bool IsSensitiveDirectory(string normalizedPath) => false;

        public Task<WebCodeCli.Domain.Repositories.Base.Workspace.WorkspaceOwnerEntity> RegisterDirectoryAsync(string directoryPath, string username, string? alias = null, bool isTrusted = false)
            => Task.FromResult(new WebCodeCli.Domain.Repositories.Base.Workspace.WorkspaceOwnerEntity());

        public Task<List<WebCodeCli.Domain.Repositories.Base.Workspace.WorkspaceOwnerEntity>> GetOwnedDirectoriesAsync(string username)
            => Task.FromResult(new List<WebCodeCli.Domain.Repositories.Base.Workspace.WorkspaceOwnerEntity>());

        public Task<WebCodeCli.Domain.Repositories.Base.Workspace.WorkspaceOwnerEntity?> GetDirectoryOwnerAsync(string directoryPath)
            => Task.FromResult<WebCodeCli.Domain.Repositories.Base.Workspace.WorkspaceOwnerEntity?>(null);

        public Task<bool> IsDirectoryOwnerAsync(string directoryPath, string username) => Task.FromResult(false);

        public Task<bool> UpdateDirectoryInfoAsync(string directoryPath, string? alias = null, bool? isTrusted = null) => Task.FromResult(true);

        public Task<bool> DeleteDirectoryAsync(string directoryPath) => Task.FromResult(true);
    }

    private sealed class StubUserWorkspacePolicyService : IUserWorkspacePolicyService
    {
        public Task<List<string>> GetAllowedDirectoriesAsync(string username) => Task.FromResult(new List<string>());

        public Task<bool> IsPathAllowedAsync(string username, string directoryPath) => Task.FromResult(true);

        public Task<bool> SaveAllowedDirectoriesAsync(string username, IEnumerable<string> allowedDirectories) => Task.FromResult(true);
    }

    private class DefaultInterfaceProxy<T> : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType ?? typeof(void);
            if (returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GenericTypeArguments[0];
                var defaultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [defaultValue]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
