using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace WebCodeCli.Helpers;

public static class DataProtectionServiceCollectionExtensions
{
    public static IServiceCollection AddWebCodeDataProtection(
        this IServiceCollection services,
        string applicationBasePath,
        string? localAppDataPath = null)
    {
        var keyDirectoryPath = ResolveKeyDirectoryPath(applicationBasePath, localAppDataPath);

        services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectoryPath));

        return services;
    }

    private static string ResolveKeyDirectoryPath(string applicationBasePath, string? localAppDataPath)
    {
        var resolvedLocalAppDataPath = string.IsNullOrWhiteSpace(localAppDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataPath;

        var keyDirectoryPath = string.IsNullOrWhiteSpace(resolvedLocalAppDataPath)
            ? Path.Combine(applicationBasePath, "DataProtection-Keys")
            : Path.Combine(resolvedLocalAppDataPath, "WebCode", "DataProtection-Keys");

        var fullPath = Path.GetFullPath(keyDirectoryPath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }
}
