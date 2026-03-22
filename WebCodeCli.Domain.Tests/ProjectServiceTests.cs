using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using System.Linq.Expressions;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Model;
using WebCodeCli.Domain.Repositories.Base;
using WebCodeCli.Domain.Repositories.Base.Project;

namespace WebCodeCli.Domain.Tests;

public class ProjectServiceTests
{
    [Fact]
    public async Task CreateProjectAsync_EmptyBranch_PersistsEmptyBranch()
    {
        var repository = new InMemoryProjectRepository();
        var service = CreateService(repository);

        var (project, errorMessage) = await service.CreateProjectAsync(new CreateProjectRequest
        {
            Name = "TfsProject",
            GitUrl = "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
            AuthType = "https",
            Branch = string.Empty
        });

        Assert.Null(errorMessage);
        Assert.NotNull(project);
        Assert.Equal(string.Empty, project!.Branch);
        Assert.Equal(string.Empty, repository.Entities.Single().Branch);
    }

    [Fact]
    public async Task UpdateProjectAsync_EmptyBranch_ClearsExistingBranch()
    {
        var repository = new InMemoryProjectRepository();
        repository.Seed(new ProjectEntity
        {
            ProjectId = "project-1",
            Username = "tester",
            Name = "Demo",
            GitUrl = "http://example/repo.git",
            AuthType = "https",
            Branch = "main",
            LocalPath = @"D:\workspace\project-1",
            Status = "pending",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        });

        var service = CreateService(repository);
        var (success, errorMessage) = await service.UpdateProjectAsync("project-1", new UpdateProjectRequest
        {
            Branch = string.Empty
        });

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.Equal(string.Empty, repository.Entities.Single().Branch);
    }

    [Fact]
    public async Task CloneProjectAsync_WhenStoredBranchEmpty_UpdatesToDetectedBranch()
    {
        var repository = new InMemoryProjectRepository();
        var tempPath = Path.Combine(Path.GetTempPath(), $"project-service-{Guid.NewGuid():N}");
        repository.Seed(new ProjectEntity
        {
            ProjectId = "project-1",
            Username = "tester",
            Name = "Demo",
            GitUrl = "http://example/repo.git",
            AuthType = "https",
            Branch = string.Empty,
            LocalPath = tempPath,
            Status = "pending",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        });

        var gitService = new StubGitService
        {
            CurrentBranch = "master"
        };
        var service = CreateService(repository, gitService);

        try
        {
            var (success, errorMessage) = await service.CloneProjectAsync("project-1");

            Assert.True(success);
            Assert.Null(errorMessage);
            Assert.Equal(string.Empty, gitService.LastCloneBranch);
            Assert.Equal("master", repository.Entities.Single().Branch);
            Assert.Equal("ready", repository.Entities.Single().Status);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CloneProjectAsync_PassesDecryptedHttpsCredentialsToGitService()
    {
        var repository = new InMemoryProjectRepository();
        var tempPath = Path.Combine(Path.GetTempPath(), $"project-service-{Guid.NewGuid():N}");
        repository.Seed(new ProjectEntity
        {
            ProjectId = "project-https",
            Username = "tester",
            Name = "Demo",
            GitUrl = "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
            AuthType = "https",
            HttpsUsername = "alice",
            HttpsToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("secret")),
            Branch = string.Empty,
            LocalPath = tempPath,
            Status = "pending",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        });

        var gitService = new StubGitService
        {
            CurrentBranch = "main"
        };
        var service = CreateService(repository, gitService);

        try
        {
            var (success, errorMessage) = await service.CloneProjectAsync("project-https");

            Assert.True(success);
            Assert.Null(errorMessage);
            Assert.NotNull(gitService.LastCloneCredentials);
            Assert.Equal("https", gitService.LastCloneCredentials!.AuthType);
            Assert.Equal("alice", gitService.LastCloneCredentials.HttpsUsername);
            Assert.Equal("secret", gitService.LastCloneCredentials.HttpsToken);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SwitchProjectBranchAsync_UpdatesStoredBranchAndPassesCredentialsToGitService()
    {
        var repository = new InMemoryProjectRepository();
        var tempPath = Path.Combine(Path.GetTempPath(), $"project-service-switch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        repository.Seed(new ProjectEntity
        {
            ProjectId = "project-switch",
            Username = "tester",
            Name = "Demo",
            GitUrl = "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
            AuthType = "https",
            HttpsUsername = "alice",
            HttpsToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("secret")),
            Branch = "main",
            LocalPath = tempPath,
            Status = "ready",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        });

        var gitService = new StubGitService
        {
            CurrentBranch = "release"
        };
        var service = CreateService(repository, gitService);

        try
        {
            var (success, errorMessage) = await service.SwitchProjectBranchAsync("project-switch", "release");

            Assert.True(success);
            Assert.Null(errorMessage);
            Assert.Equal("release", gitService.LastSwitchedBranch);
            Assert.NotNull(gitService.LastSwitchCredentials);
            Assert.Equal("alice", gitService.LastSwitchCredentials!.HttpsUsername);
            Assert.Equal("secret", gitService.LastSwitchCredentials.HttpsToken);
            Assert.Equal("release", repository.Entities.Single().Branch);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    private static ProjectService CreateService(InMemoryProjectRepository repository, StubGitService? gitService = null)
    {
        return new ProjectService(
            repository,
            new StubUserContextService(),
            new StubSystemSettingsService(),
            gitService ?? new StubGitService(),
            new StubWorkspaceRegistryService(),
            NullLogger<ProjectService>.Instance);
    }

    private sealed class StubUserContextService : IUserContextService
    {
        public string GetCurrentUsername() => "tester";

        public string GetCurrentRole() => "admin";

        public bool IsAuthenticated() => true;

        public void SetCurrentUsername(string username)
        {
        }
    }

    private sealed class StubSystemSettingsService : ISystemSettingsService
    {
        public Task<bool> IsSystemInitializedAsync() => throw new NotSupportedException();

        public Task<bool> CompleteInitializationAsync(SystemInitConfig config) => throw new NotSupportedException();

        public Task<string> GetWorkspaceRootAsync() => Task.FromResult(@"D:\workspace");

        public Task<bool> SetWorkspaceRootAsync(string path) => throw new NotSupportedException();

        public Task<SystemConfigSummary> GetConfigSummaryAsync() => throw new NotSupportedException();

        public Task<bool> ValidateInitPasswordAsync(string username, string password) => throw new NotSupportedException();

        public Task<bool> UpdateAdminCredentialsAsync(string username, string password) => throw new NotSupportedException();

        public Task<List<string>> GetEnabledAssistantsAsync() => throw new NotSupportedException();

        public Task<bool> SaveEnabledAssistantsAsync(List<string> assistants) => throw new NotSupportedException();

        public Task<LocalCliConfigDetectionResult> DetectLocalCliConfigsAsync() => throw new NotSupportedException();
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

    private sealed class StubGitService : IGitService
    {
        public string? CurrentBranch { get; set; }

        public string? LastCloneBranch { get; private set; }

        public GitCredentials? LastCloneCredentials { get; private set; }

        public string? LastSwitchedBranch { get; private set; }

        public GitCredentials? LastSwitchCredentials { get; private set; }

        public bool IsGitRepository(string workspacePath) => true;

        public Task<List<GitCommit>> GetFileHistoryAsync(string workspacePath, string filePath, int maxCount = 50) => throw new NotSupportedException();

        public Task<string> GetFileContentAtCommitAsync(string workspacePath, string filePath, string commitHash) => throw new NotSupportedException();

        public Task<GitDiffResult> GetFileDiffAsync(string workspacePath, string filePath, string fromCommit, string toCommit) => throw new NotSupportedException();

        public Task<GitStatus> GetWorkspaceStatusAsync(string workspacePath) => throw new NotSupportedException();

        public Task<List<GitCommit>> GetAllCommitsAsync(string workspacePath, int maxCount = 100) => throw new NotSupportedException();

        public Task<(bool Success, string? ErrorMessage)> CloneAsync(string gitUrl, string localPath, string branch, GitCredentials? credentials = null, Action<CloneProgress>? progress = null)
        {
            LastCloneBranch = branch;
            LastCloneCredentials = credentials;
            Directory.CreateDirectory(localPath);
            return Task.FromResult((true, (string?)null));
        }

        public Task<(bool Success, string? ErrorMessage)> PullAsync(string localPath, GitCredentials? credentials = null)
            => Task.FromResult((true, (string?)null));

        public Task<(List<string> Branches, string? ErrorMessage)> ListRemoteBranchesAsync(string gitUrl, GitCredentials? credentials = null)
            => Task.FromResult<(List<string>, string?)>(([], null));

        public string? GetCurrentBranch(string localPath) => CurrentBranch;

        public Task<(bool Success, string? ErrorMessage)> SwitchBranchAsync(string localPath, string branch, GitCredentials? credentials = null)
        {
            LastSwitchedBranch = branch;
            LastSwitchCredentials = credentials;
            CurrentBranch = branch;
            return Task.FromResult((true, (string?)null));
        }
    }

    private sealed class InMemoryProjectRepository : IProjectRepository
    {
        private readonly List<ProjectEntity> _entities = [];

        public IReadOnlyList<ProjectEntity> Entities => _entities;

        public void Seed(ProjectEntity entity)
        {
            _entities.Add(Clone(entity));
        }

        public Task<List<ProjectEntity>> GetByUsernameAsync(string username)
            => Task.FromResult(_entities.Where(entity => entity.Username == username).Select(Clone).ToList());

        public Task<ProjectEntity?> GetByIdAndUsernameAsync(string projectId, string username)
        {
            var entity = _entities.FirstOrDefault(item => item.ProjectId == projectId && item.Username == username);
            return Task.FromResult(entity == null ? null : Clone(entity));
        }

        public Task<bool> DeleteByIdAndUsernameAsync(string projectId, string username)
        {
            var removed = _entities.RemoveAll(item => item.ProjectId == projectId && item.Username == username) > 0;
            return Task.FromResult(removed);
        }

        public Task<List<ProjectEntity>> GetByUsernameOrderByUpdatedAtAsync(string username)
            => Task.FromResult(_entities.Where(entity => entity.Username == username).Select(Clone).OrderByDescending(entity => entity.UpdatedAt).ToList());

        public Task<bool> ExistsByNameAndUsernameAsync(string name, string username, string? excludeProjectId = null)
            => Task.FromResult(_entities.Any(entity =>
                entity.Username == username
                && string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(entity.ProjectId, excludeProjectId, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> InsertAsync(ProjectEntity obj)
        {
            _entities.Add(Clone(obj));
            return Task.FromResult(true);
        }

        public Task<bool> UpdateAsync(ProjectEntity obj)
        {
            var index = _entities.FindIndex(entity => entity.ProjectId == obj.ProjectId && entity.Username == obj.Username);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _entities[index] = Clone(obj);
            return Task.FromResult(true);
        }

        private static ProjectEntity Clone(ProjectEntity entity)
        {
            return new ProjectEntity
            {
                ProjectId = entity.ProjectId,
                Username = entity.Username,
                Name = entity.Name,
                GitUrl = entity.GitUrl,
                AuthType = entity.AuthType,
                HttpsUsername = entity.HttpsUsername,
                HttpsToken = entity.HttpsToken,
                SshPrivateKey = entity.SshPrivateKey,
                SshPassphrase = entity.SshPassphrase,
                Branch = entity.Branch,
                LocalPath = entity.LocalPath,
                LastSyncAt = entity.LastSyncAt,
                Status = entity.Status,
                ErrorMessage = entity.ErrorMessage,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }

        public SqlSugarScope GetDB() => throw new NotSupportedException();
        public List<ProjectEntity> GetList() => throw new NotSupportedException();
        public Task<List<ProjectEntity>> GetListAsync() => throw new NotSupportedException();
        public List<ProjectEntity> GetList(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<List<ProjectEntity>> GetListAsync(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public int Count(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<int> CountAsync(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public PageList<ProjectEntity> GetPageList(Expression<Func<ProjectEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(Expression<Func<ProjectEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ProjectEntity>> GetPageListAsync(Expression<Func<ProjectEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(Expression<Func<ProjectEntity, bool>> whereExpression, PageModel page) => throw new NotSupportedException();
        public PageList<ProjectEntity> GetPageList(Expression<Func<ProjectEntity, bool>> whereExpression, PageModel page, Expression<Func<ProjectEntity, object>> orderByExpression = null!, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ProjectEntity>> GetPageListAsync(Expression<Func<ProjectEntity, bool>> whereExpression, PageModel page, Expression<Func<ProjectEntity, object>> orderByExpression = null!, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<P> GetPageList<P>(Expression<Func<ProjectEntity, bool>> whereExpression, PageModel page, Expression<Func<ProjectEntity, object>> orderByExpression = null!, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<P>> GetPageListAsync<P>(Expression<Func<ProjectEntity, bool>> whereExpression, PageModel page, Expression<Func<ProjectEntity, object>> orderByExpression = null!, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public PageList<ProjectEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public Task<PageList<ProjectEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page) => throw new NotSupportedException();
        public PageList<ProjectEntity> GetPageList(List<IConditionalModel> conditionalList, PageModel page, Expression<Func<ProjectEntity, object>> orderByExpression = null!, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public Task<PageList<ProjectEntity>> GetPageListAsync(List<IConditionalModel> conditionalList, PageModel page, Expression<Func<ProjectEntity, object>> orderByExpression = null!, OrderByType orderByType = OrderByType.Asc) => throw new NotSupportedException();
        public ProjectEntity GetById(dynamic id) => throw new NotSupportedException();
        public Task<ProjectEntity> GetByIdAsync(dynamic id) => throw new NotSupportedException();
        public ProjectEntity GetSingle(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<ProjectEntity> GetSingleAsync(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public ProjectEntity GetFirst(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<ProjectEntity> GetFirstAsync(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public bool Insert(ProjectEntity obj) => throw new NotSupportedException();
        public bool InsertRange(List<ProjectEntity> objs) => throw new NotSupportedException();
        public Task<bool> InsertRangeAsync(List<ProjectEntity> objs) => throw new NotSupportedException();
        public int InsertReturnIdentity(ProjectEntity obj) => throw new NotSupportedException();
        public Task<int> InsertReturnIdentityAsync(ProjectEntity obj) => throw new NotSupportedException();
        public long InsertReturnBigIdentity(ProjectEntity obj) => throw new NotSupportedException();
        public Task<long> InsertReturnBigIdentityAsync(ProjectEntity obj) => throw new NotSupportedException();
        public bool DeleteByIds(dynamic[] ids) => throw new NotSupportedException();
        public Task<bool> DeleteByIdsAsync(dynamic[] ids) => throw new NotSupportedException();
        public bool Delete(dynamic id) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(dynamic id) => throw new NotSupportedException();
        public bool Delete(ProjectEntity obj) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(ProjectEntity obj) => throw new NotSupportedException();
        public bool Delete(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public bool Update(ProjectEntity obj) => throw new NotSupportedException();
        public bool UpdateRange(List<ProjectEntity> objs) => throw new NotSupportedException();
        public bool InsertOrUpdate(ProjectEntity obj) => throw new NotSupportedException();
        public Task<bool> InsertOrUpdateAsync(ProjectEntity obj) => throw new NotSupportedException();
        public Task<bool> UpdateRangeAsync(List<ProjectEntity> objs) => throw new NotSupportedException();
        public bool IsAny(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
        public Task<bool> IsAnyAsync(Expression<Func<ProjectEntity, bool>> whereExpression) => throw new NotSupportedException();
    }
}
