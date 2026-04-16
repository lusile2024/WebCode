using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebCodeCli.Helpers;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class DataProtectionServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), "DataProtectionServiceCollectionExtensionsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddWebCodeDataProtection_PersistsKeysUnderExplicitLocalAppDataDirectory()
    {
        var services = new ServiceCollection();
        var appBaseDirectory = CreateDirectory("runtime");
        var localAppDataDirectory = CreateDirectory("local-app-data");

        services.AddWebCodeDataProtection(appBaseDirectory, localAppDataDirectory);

        using var serviceProvider = services.BuildServiceProvider();
        var keyManagementOptions = serviceProvider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        var repository = Assert.IsType<FileSystemXmlRepository>(keyManagementOptions.XmlRepository);
        var expectedDirectory = Path.Combine(Path.GetFullPath(localAppDataDirectory), "WebCode", "DataProtection-Keys");

        Assert.Equal(expectedDirectory, repository.Directory.FullName);
        Assert.True(Directory.Exists(expectedDirectory));
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
