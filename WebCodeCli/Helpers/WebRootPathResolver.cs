namespace WebCodeCli.Helpers;

public static class WebRootPathResolver
{
    public static string? Resolve(string? currentWebRootPath, string contentRootPath, string applicationBasePath)
    {
        var candidatePaths = EnumerateCandidatePaths(currentWebRootPath, contentRootPath, applicationBasePath);

        return candidatePaths.FirstOrDefault(Directory.Exists);
    }

    private static IEnumerable<string> EnumerateCandidatePaths(
        string? currentWebRootPath,
        string contentRootPath,
        string applicationBasePath)
    {
        var rawCandidates = new[]
        {
            currentWebRootPath,
            Path.Combine(contentRootPath, "wwwroot"),
            Path.Combine(applicationBasePath, "wwwroot"),
            Path.Combine(contentRootPath, "WebCodeCli", "wwwroot"),
            Path.GetFullPath(Path.Combine(applicationBasePath, "..", "..", "WebCodeCli", "wwwroot")),
            Path.GetFullPath(Path.Combine(applicationBasePath, "..", "..", "..", "wwwroot")),
            Path.GetFullPath(Path.Combine(applicationBasePath, "..", "..", "..", "WebCodeCli", "wwwroot")),
            Path.GetFullPath(Path.Combine(applicationBasePath, "..", "..", "..", "..", "wwwroot"))
        };

        foreach (var candidate in rawCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            yield return Path.GetFullPath(candidate);
        }
    }
}
