using System.Data.SQLite;
using Microsoft.Extensions.Logging.Abstractions;
using WebCodeCli.Services;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class CcSwitchServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), "CcSwitchServiceTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetToolStatusAsync_ReturnsToolSpecificMessage_WhenProviderIsMissing()
    {
        var configDirectory = CreateDirectory(".cc-switch");
        CreateDatabase(Path.Combine(configDirectory, "cc-switch.db"));
        var service = new CcSwitchService(NullLogger<CcSwitchService>.Instance, () => configDirectory);

        var status = await service.GetToolStatusAsync("opencode");

        Assert.False(status.IsLaunchReady);
        Assert.Equal("OpenCode 当前未在 cc-switch 中激活 Provider。", status.StatusMessage);
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

    private static void CreateDatabase(string databasePath)
    {
        SQLiteConnection.CreateFile(databasePath);
        using var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE providers (
                id TEXT PRIMARY KEY,
                app_type TEXT NOT NULL,
                name TEXT NOT NULL,
                category TEXT NULL,
                is_current INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
    }
}
