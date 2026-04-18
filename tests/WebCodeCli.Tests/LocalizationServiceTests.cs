using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.JSInterop;
using WebCodeCli.Domain.Domain.Service;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class LocalizationServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "LocalizationServiceTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetTranslationAsync_PrefersWebRootLocalizationFile_WhenAvailable()
    {
        var webRoot = Path.Combine(_testRoot, "wwwroot");
        var localizationDirectory = Path.Combine(webRoot, "Resources", "Localization");
        Directory.CreateDirectory(localizationDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(localizationDirectory, "zh-CN.json"),
            """
            {
              "setup": {
                "continueToReview": "查看 cc-switch 状态",
                "refreshStatus": "刷新状态"
              }
            }
            """);

        var jsRuntime = new RecordingJsRuntime(
            """
            {
              "setup": {
                "continueToReview": "stale",
                "refreshStatus": "stale"
              }
            }
            """);

        var service = new LocalizationService(jsRuntime, new FakeWebHostEnvironment(webRoot));

        var refreshStatus = await service.GetTranslationAsync("setup.refreshStatus", "zh-CN");
        var continueToReview = await service.GetTranslationAsync("setup.continueToReview", "zh-CN");

        Assert.Equal("刷新状态", refreshStatus);
        Assert.Equal("查看 cc-switch 状态", continueToReview);
        Assert.DoesNotContain("localizationHelper.fetchTranslationFile", jsRuntime.Invocations);
        Assert.Contains("localizationHelper.loadTranslations", jsRuntime.Invocations);
    }

    [Fact]
    public void ServiceWorker_UsesNetworkFirstForLocalizationResources()
    {
        var content = File.ReadAllText(@"D:\VSWorkshop\WebCode\WebCodeCli\wwwroot\service-worker.js");

        Assert.Contains("'/Resources/Localization/'", content);
        Assert.DoesNotContain("'/Resources/Localization/zh-CN.json'", content);
        Assert.DoesNotContain("'/Resources/Localization/en-US.json'", content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private sealed class RecordingJsRuntime(string fetchResponse) : IJSRuntime
    {
        public List<string> Invocations { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Invocations.Add(identifier);

            if (identifier == "localizationHelper.fetchTranslationFile" && typeof(TValue) == typeof(string))
            {
                return new ValueTask<TValue>((TValue)(object)fetchResponse);
            }

            return new ValueTask<TValue>(default(TValue)!);
        }
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string webRootPath)
        {
            WebRootPath = webRootPath;
            WebRootFileProvider = new PhysicalFileProvider(webRootPath);
            ContentRootPath = webRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(webRootPath);
        }

        public string ApplicationName { get; set; } = "WebCodeCli.Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
