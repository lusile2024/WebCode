using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Domain.Tests;

public class GitServiceTests
{
    [Fact]
    public async Task CloneAsync_HttpCredentialsWithoutBranch_UsesCredentialApproveAndRemoteDefaultBranch()
    {
        var service = new InspectableGitService();
        var localPath = Path.Combine(Path.GetTempPath(), $"git-clone-{Guid.NewGuid():N}");

        try
        {
            var (success, errorMessage) = await service.CloneAsync(
                "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
                localPath,
                string.Empty,
                new GitCredentials
                {
                    AuthType = "https",
                    HttpsUsername = "alice",
                    HttpsToken = "secret"
                });

            Assert.True(success);
            Assert.Null(errorMessage);
            Assert.Collection(
                service.Calls,
                approveCall =>
                {
                    Assert.Contains("credential approve", approveCall.Arguments);
                    Assert.Contains("credential.helper=store --file=", approveCall.Arguments);
                    Assert.Contains("credential.useHttpPath=true", approveCall.Arguments);
                    Assert.Equal(
                        "protocol=http" + Environment.NewLine +
                        "host=sql-for-tfs2017:8080" + Environment.NewLine +
                        "path=tfs/DefaultCollection/WmsV4/_git/WmsServerV4" + Environment.NewLine +
                        "username=alice" + Environment.NewLine +
                        "password=secret" + Environment.NewLine +
                        Environment.NewLine,
                        approveCall.StandardInput);
                    Assert.Equal("never", approveCall.AdditionalEnvironment?["GCM_INTERACTIVE"]);
                },
                cloneCall =>
                {
                    Assert.Contains("credential.helper=store --file=", cloneCall.Arguments);
                    Assert.EndsWith(
                        $"clone \"http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4\" \"{localPath}\"",
                        cloneCall.Arguments,
                        StringComparison.Ordinal);
                    Assert.DoesNotContain("alice:secret@", cloneCall.Arguments, StringComparison.Ordinal);
                });
        }
        finally
        {
            if (Directory.Exists(localPath))
            {
                Directory.Delete(localPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ListRemoteBranchesAsync_HttpCredentials_UsesTemporaryCredentialStoreAndParsesBranches()
    {
        var service = new InspectableGitService();
        service.Results.Enqueue((0, string.Empty, string.Empty));
        service.Results.Enqueue((0, "sha-main\trefs/heads/main\nsha-release refs/heads/release\n", string.Empty));

        var (branches, errorMessage) = await service.ListRemoteBranchesAsync(
            "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4",
            new GitCredentials
            {
                AuthType = "https",
                HttpsUsername = "alice",
                HttpsToken = "secret"
            });

        Assert.Null(errorMessage);
        Assert.Equal(["main", "release"], branches);
        Assert.Collection(
            service.Calls,
            approveCall => Assert.Contains("credential approve", approveCall.Arguments),
            listCall =>
            {
                Assert.EndsWith(
                    "ls-remote --heads \"http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4\"",
                    listCall.Arguments,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("alice:secret@", listCall.Arguments, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task PullAsync_HttpCredentials_UsesTemporaryCredentialStore()
    {
        var repoPath = Path.Combine(Path.GetTempPath(), $"git-pull-{Guid.NewGuid():N}");
        Repository.Init(repoPath);

        try
        {
            using (var repo = new Repository(repoPath))
            {
                repo.Network.Remotes.Add("origin", "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4");
            }

            var service = new InspectableGitService();
            var (success, errorMessage) = await service.PullAsync(
                repoPath,
                new GitCredentials
                {
                    AuthType = "https",
                    HttpsUsername = "alice",
                    HttpsToken = "secret"
                });

            Assert.True(success);
            Assert.Null(errorMessage);
            Assert.Collection(
                service.Calls,
                approveCall => Assert.Contains("credential approve", approveCall.Arguments),
                pullCall => Assert.EndsWith("pull", pullCall.Arguments, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(repoPath))
            {
                Directory.Delete(repoPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SwitchBranchAsync_HttpCredentials_UsesCliCheckoutWithLongPathSupport()
    {
        var repoPath = Path.Combine(Path.GetTempPath(), $"git-switch-{Guid.NewGuid():N}");
        Repository.Init(repoPath);

        try
        {
            using (var repo = new Repository(repoPath))
            {
                repo.Network.Remotes.Add("origin", "http://sql-for-tfs2017:8080/tfs/DefaultCollection/WmsV4/_git/WmsServerV4");
            }

            var service = new InspectableGitService();
            var (success, errorMessage) = await service.SwitchBranchAsync(
                repoPath,
                "release",
                new GitCredentials
                {
                    AuthType = "https",
                    HttpsUsername = "alice",
                    HttpsToken = "secret"
                });

            Assert.True(success);
            Assert.Null(errorMessage);
            Assert.Collection(
                service.Calls,
                approveFetchCall => Assert.Contains("credential approve", approveFetchCall.Arguments),
                fetchCall =>
                {
                    Assert.Contains("-c core.longpaths=true", fetchCall.Arguments);
                    Assert.EndsWith(
                        "fetch origin \"refs/heads/release:refs/remotes/origin/release\"",
                        fetchCall.Arguments,
                        StringComparison.Ordinal);
                },
                approveCheckoutCall => Assert.Contains("credential approve", approveCheckoutCall.Arguments),
                checkoutCall =>
                {
                    Assert.Contains("-c core.longpaths=true", checkoutCall.Arguments);
                    Assert.EndsWith(
                        "checkout -b \"release\" --track \"origin/release\"",
                        checkoutCall.Arguments,
                        StringComparison.Ordinal);
                });
        }
        finally
        {
            if (Directory.Exists(repoPath))
            {
                Directory.Delete(repoPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteGitCommand_WhenCommandWritesLargeStdErr_CompletesWithoutDeadlock()
    {
        var repoPath = Path.Combine(Path.GetTempPath(), $"git-exec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repoPath);
        Repository.Init(repoPath);
        var gitSpamPath = Path.Combine(repoPath, "spam.cmd");

        try
        {
            await File.WriteAllTextAsync(
                gitSpamPath,
                "@echo off\r\nfor /L %%i in (1,1,20000) do @echo err 1>&2\r\necho ok");

            var escapedScriptPath = gitSpamPath.Replace("\\", "\\\\");
            using (var repo = new Repository(repoPath))
            {
                repo.Config.Set("alias.spam", $"!cmd //c call {escapedScriptPath}");
            }

            var service = new InspectableGitService();
            var result = await Task.Run(() => service.InvokeExecuteGitCommand(
                repoPath,
                "spam")).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("ok", result.StdOut);
            Assert.Contains("err", result.StdErr);
        }
        finally
        {
            if (Directory.Exists(repoPath))
            {
                Directory.Delete(repoPath, recursive: true);
            }
        }
    }

    private sealed class InspectableGitService : GitService
    {
        public InspectableGitService()
            : base(NullLogger<GitService>.Instance)
        {
        }

        public List<GitCommandCall> Calls { get; } = [];

        public Queue<(int ExitCode, string StdOut, string StdErr)> Results { get; } = [];

        public (int ExitCode, string StdOut, string StdErr) InvokeExecuteGitCommand(
            string workingDirectory,
            string arguments,
            string? sshKeyPath = null,
            string? sshAskPassPath = null,
            string? standardInput = null,
            IReadOnlyDictionary<string, string>? additionalEnvironment = null)
        {
            return base.ExecuteGitCommand(
                workingDirectory,
                arguments,
                sshKeyPath,
                sshAskPassPath,
                standardInput,
                additionalEnvironment);
        }

        protected override (int ExitCode, string StdOut, string StdErr) ExecuteGitCommand(
            string workingDirectory,
            string arguments,
            string? sshKeyPath,
            string? sshAskPassPath,
            string? standardInput = null,
            IReadOnlyDictionary<string, string>? additionalEnvironment = null)
        {
            Calls.Add(new GitCommandCall(arguments, standardInput, additionalEnvironment));

            if (arguments.Contains(" clone ", StringComparison.Ordinal) && !Directory.Exists(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
            }

            if (Results.Count > 0)
            {
                return Results.Dequeue();
            }

            return (0, string.Empty, string.Empty);
        }
    }

    private sealed record GitCommandCall(
        string Arguments,
        string? StandardInput,
        IReadOnlyDictionary<string, string>? AdditionalEnvironment);
}
