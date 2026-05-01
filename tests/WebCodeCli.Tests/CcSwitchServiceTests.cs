using System.Data.SQLite;
using System.Net;
using System.Net.Http;
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

    [Fact]
    public async Task GetModelCatalogAsync_ReturnsRemoteModels_ForEffectiveProvider()
    {
        var configDirectory = CreateDirectory(".cc-switch");
        var databasePath = Path.Combine(configDirectory, "cc-switch.db");
        CreateDatabase(databasePath);
        InsertProvider(
            databasePath,
            id: "provider-codex",
            appType: "codex",
            name: "Codex Provider",
            category: "openai",
            isCurrent: 1,
            settingsConfig: """
                {"auth":{"OPENAI_API_KEY":"test-key"},"config":"model_provider = \"unit\"\nmodel = \"gpt-5.4\"\n[model_providers.unit]\nbase_url = \"https://unit.test/v1\"\n"}
                """);

        var service = new CcSwitchService(
            NullLogger<CcSwitchService>.Instance,
            () => configDirectory,
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"id":"gpt-5.4"},{"id":"gpt-5.4-mini","display_name":"GPT 5.4 Mini"}]}""")
            }));

        var catalog = await service.GetModelCatalogAsync("codex");

        Assert.True(catalog.IsRemoteFetched);
        Assert.Equal("provider-codex", catalog.ProviderId);
        Assert.Collection(
            catalog.Models,
            option => Assert.Equal("gpt-5.4", option.Id),
            option =>
            {
                Assert.Equal("gpt-5.4-mini", option.Id);
                Assert.Equal("GPT 5.4 Mini", option.DisplayName);
            });
    }

    [Fact]
    public async Task GetModelCatalogAsync_FallsBackToConfiguredModel_WhenRemoteFetchUnavailable()
    {
        var configDirectory = CreateDirectory(".cc-switch");
        var databasePath = Path.Combine(configDirectory, "cc-switch.db");
        CreateDatabase(databasePath);
        InsertProvider(
            databasePath,
            id: "provider-claude",
            appType: "claude",
            name: "Claude Provider",
            category: "anthropic",
            isCurrent: 1,
            settingsConfig: """
                {"env":{"ANTHROPIC_AUTH_TOKEN":"test-key","ANTHROPIC_BASE_URL":"https://anthropic.unit","ANTHROPIC_MODEL":"claude-sonnet-4-6"}}
                """);

        var service = new CcSwitchService(NullLogger<CcSwitchService>.Instance, () => configDirectory);

        var catalog = await service.GetModelCatalogAsync("claude-code");

        Assert.False(catalog.IsRemoteFetched);
        var option = Assert.Single(catalog.Models);
        Assert.Equal("claude-sonnet-4-6", option.Id);
        Assert.Contains("默认模型", catalog.StatusMessage);
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
                settings_config TEXT NULL,
                provider_type TEXT NULL,
                meta TEXT NULL,
                is_current INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE provider_endpoints (
                provider_id TEXT NOT NULL,
                app_type TEXT NOT NULL,
                url TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertProvider(
        string databasePath,
        string id,
        string appType,
        string name,
        string? category,
        int isCurrent,
        string? settingsConfig)
    {
        using var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO providers (id, app_type, name, category, settings_config, provider_type, meta, is_current)
            VALUES (@id, @appType, @name, @category, @settingsConfig, NULL, NULL, @isCurrent);
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@appType", appType);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@category", (object?)category ?? DBNull.Value);
        command.Parameters.AddWithValue("@settingsConfig", (object?)settingsConfig ?? DBNull.Value);
        command.Parameters.AddWithValue("@isCurrent", isCurrent);
        command.ExecuteNonQuery();
    }

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHttpMessageHandler(responseFactory), disposeHandler: true);
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
