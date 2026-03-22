using Xunit;
using WebCodeCli.Helpers;

namespace WebCodeCli.Tests;

public sealed class WebRootPathResolverTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), "WebRootPathResolverTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_ReturnsExistingWebRootPath_WhenAlreadyValid()
    {
        var existingWebRoot = CreateDirectory("existing-wwwroot");

        var resolved = WebRootPathResolver.Resolve(existingWebRoot, CreateDirectory("content-root"), CreateDirectory("app-base"));

        Assert.Equal(Path.GetFullPath(existingWebRoot), resolved);
    }

    [Fact]
    public void Resolve_PrefersPublishedOutputWebRoot_WhenPresent()
    {
        var contentRoot = CreateDirectory("worktree-root");
        var appBase = CreateDirectory("artifacts", "auth-runtime-release");
        var publishedWebRoot = CreateDirectory("artifacts", "auth-runtime-release", "wwwroot");
        CreateDirectory("worktree-root", "WebCodeCli", "wwwroot");

        var resolved = WebRootPathResolver.Resolve(null, contentRoot, appBase);

        Assert.Equal(Path.GetFullPath(publishedWebRoot), resolved);
    }

    [Fact]
    public void Resolve_FallsBackToProjectWebRoot_WhenStartedFromSolutionRoot()
    {
        var contentRoot = CreateDirectory("solution-root");
        var projectWebRoot = CreateDirectory("solution-root", "WebCodeCli", "wwwroot");
        var appBase = CreateDirectory("solution-root", "artifacts", "publish");

        var resolved = WebRootPathResolver.Resolve(null, contentRoot, appBase);

        Assert.Equal(Path.GetFullPath(projectWebRoot), resolved);
    }

    [Fact]
    public void Resolve_FallsBackToProjectWebRoot_WhenStartedFromBuildOutputWithoutCopiedAssets()
    {
        var contentRoot = CreateDirectory("WebCodeCli", "bin", "Debug", "net10.0");
        var appBase = contentRoot;
        var projectWebRoot = CreateDirectory("WebCodeCli", "wwwroot");

        var resolved = WebRootPathResolver.Resolve(null, contentRoot, appBase);

        Assert.Equal(Path.GetFullPath(projectWebRoot), resolved);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private string CreateDirectory(params string[] segments)
    {
        var path = Path.Combine(new[] { _testRoot }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }
}
