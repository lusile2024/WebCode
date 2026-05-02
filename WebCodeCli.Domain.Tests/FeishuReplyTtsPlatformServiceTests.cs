using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public sealed class FeishuReplyTtsPlatformServiceTests
{
    [Fact]
    public async Task GetHealthAsync_WhenStorageRootResolutionFails_ReturnsUnavailable()
    {
        var resolver = CreateUnavailableResolver();
        var meloClient = new StubMeloTtsClient();
        var service = CreateService(resolver, meloClient);

        var result = await service.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsAvailable);
        Assert.Contains("AllowSystemDriveInstall", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, meloClient.HealthCallCount);
    }

    [Fact]
    public async Task GetHealthAsync_MergesResolverAvailabilityWithLocalServiceHealth()
    {
        var resolver = CreateAvailableResolver();
        var meloClient = new StubMeloTtsClient
        {
            HealthResult = new FeishuReplyTtsHealthStatus
            {
                IsAvailable = true,
                ServiceStatus = "ok",
                Device = "cpu",
                DefaultVoiceId = "service-default"
            }
        };
        var service = CreateService(resolver, meloClient);

        var result = await service.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.Equal(@"D:\reply-tts", result.StorageRoot);
        Assert.Equal(@"D:\reply-tts\models", result.ModelsRoot);
        Assert.Equal("ok", result.ServiceStatus);
        Assert.Equal("cpu", result.Device);
        Assert.Equal("service-default", result.DefaultVoiceId);
    }

    [Fact]
    public async Task GetHealthAsync_WhenConfiguredDefaultVoiceExists_PrefersConfiguredDefault()
    {
        var service = CreateService(
            CreateAvailableResolver(),
            new StubMeloTtsClient
            {
                HealthResult = new FeishuReplyTtsHealthStatus
                {
                    IsAvailable = true,
                    ServiceStatus = "ok",
                    DefaultVoiceId = "service-default"
                }
            },
            defaultVoiceId: "configured-default");

        var result = await service.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal("configured-default", result.DefaultVoiceId);
    }

    [Fact]
    public async Task GetVoicesAsync_ReturnsRuntimeVoiceList()
    {
        var resolver = CreateAvailableResolver();
        var meloClient = new StubMeloTtsClient
        {
            VoicesResult =
            [
                new FeishuReplyTtsVoiceOption
                {
                    VoiceId = "voice-a",
                    DisplayName = "Voice A"
                },
                new FeishuReplyTtsVoiceOption
                {
                    VoiceId = "voice-b",
                    DisplayName = "Voice B"
                }
            ]
        };
        var service = CreateService(resolver, meloClient);

        var result = await service.GetVoicesAsync(TestContext.Current.CancellationToken);

        Assert.Collection(
            result,
            voice => Assert.Equal("voice-a", voice.VoiceId),
            voice => Assert.Equal("voice-b", voice.VoiceId));
    }

    [Fact]
    public async Task GetVoicesAsync_WhenLocalServiceIsUnreachable_ReturnsEmptyList()
    {
        var service = CreateService(
            CreateAvailableResolver(),
            new StubMeloTtsClient
            {
                VoicesException = new HttpRequestException("connection refused")
            });

        var result = await service.GetVoicesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveVoiceOrFallbackAsync_WhenSavedVoiceExists_PrefersSavedVoice()
    {
        var service = CreateService(
            CreateAvailableResolver(),
            new StubMeloTtsClient
            {
                VoicesResult =
                [
                    new FeishuReplyTtsVoiceOption { VoiceId = "saved-voice", DisplayName = "Saved" },
                    new FeishuReplyTtsVoiceOption { VoiceId = "default-voice", DisplayName = "Default" }
                ]
            },
            defaultVoiceId: "default-voice");

        var result = await service.ResolveVoiceOrFallbackAsync("saved-voice", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("saved-voice", result.VoiceId);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task ResolveVoiceOrFallbackAsync_WhenSavedVoiceIsMissing_UsesDefaultVoice()
    {
        var service = CreateService(
            CreateAvailableResolver(),
            new StubMeloTtsClient
            {
                HealthResult = new FeishuReplyTtsHealthStatus
                {
                    IsAvailable = true,
                    ServiceStatus = "ok",
                    DefaultVoiceId = "service-default"
                },
                VoicesResult =
                [
                    new FeishuReplyTtsVoiceOption { VoiceId = "default-zh", DisplayName = "Default" }
                ]
            },
            defaultVoiceId: "default-zh");

        var result = await service.ResolveVoiceOrFallbackAsync("missing-voice", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("default-zh", result.VoiceId);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task ResolveVoiceOrFallbackAsync_WhenConfiguredDefaultIsBlank_UsesServiceDefaultVoice()
    {
        var service = CreateService(
            CreateAvailableResolver(),
            new StubMeloTtsClient
            {
                HealthResult = new FeishuReplyTtsHealthStatus
                {
                    IsAvailable = true,
                    ServiceStatus = "ok",
                    DefaultVoiceId = "service-default"
                },
                VoicesResult =
                [
                    new FeishuReplyTtsVoiceOption { VoiceId = "service-default", DisplayName = "Service Default" }
                ]
            });

        var result = await service.ResolveVoiceOrFallbackAsync("missing-voice", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("service-default", result.VoiceId);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task ResolveVoiceOrFallbackAsync_WhenNoSavedOrDefaultVoiceMatches_FailsCleanly()
    {
        var service = CreateService(
            CreateAvailableResolver(),
            new StubMeloTtsClient
            {
                VoicesResult =
                [
                    new FeishuReplyTtsVoiceOption { VoiceId = "voice-a", DisplayName = "Voice A" }
                ]
            },
            defaultVoiceId: "default-zh");

        var result = await service.ResolveVoiceOrFallbackAsync("missing-voice", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Null(result.VoiceId);
        Assert.Contains("No Feishu reply TTS voice", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveVoiceOrFallbackAsync_WhenLocalServiceIsUnreachable_FailsCleanly()
    {
        var service = CreateService(
            CreateAvailableResolver(),
            new StubMeloTtsClient
            {
                VoicesException = new HttpRequestException("connection refused")
            },
            defaultVoiceId: "configured-default");

        var result = await service.ResolveVoiceOrFallbackAsync("saved-voice", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Null(result.VoiceId);
        Assert.Equal("Feishu reply TTS voices are currently unavailable.", result.Message);
    }

    private static FeishuReplyTtsPlatformService CreateService(
        ReplyTtsStorageRootResolver resolver,
        StubMeloTtsClient meloClient,
        string? defaultVoiceId = null)
    {
        return new FeishuReplyTtsPlatformService(
            resolver,
            Options.Create(new FeishuReplyTtsOptions
            {
                TtsDefaultVoiceId = defaultVoiceId
            }),
            meloClient);
    }

    private static ReplyTtsStorageRootResolver CreateAvailableResolver()
    {
        return new ReplyTtsStorageRootResolver(
            new MutableOptionsMonitor<FeishuReplyTtsOptions>(new FeishuReplyTtsOptions
            {
                TtsStorageRoot = @"D:\reply-tts"
            }),
            new FakeReplyTtsHostEnvironment(
                isWindows: true,
                systemDriveRoot: @"C:\",
                drives:
                [
                    new ReplyTtsDriveDescriptor(@"D:\", isReady: true, isWritable: true)
                ]));
    }

    private static ReplyTtsStorageRootResolver CreateUnavailableResolver()
    {
        return new ReplyTtsStorageRootResolver(
            new MutableOptionsMonitor<FeishuReplyTtsOptions>(new FeishuReplyTtsOptions
            {
                AllowSystemDriveInstall = false
            }),
            new FakeReplyTtsHostEnvironment(
                isWindows: true,
                systemDriveRoot: @"C:\",
                drives:
                [
                    new ReplyTtsDriveDescriptor(@"C:\", isReady: true, isWritable: true)
                ]));
    }

    private sealed class StubMeloTtsClient : IMeloTtsClient
    {
        public int HealthCallCount { get; private set; }

        public FeishuReplyTtsHealthStatus HealthResult { get; set; } = new();

        public IReadOnlyList<FeishuReplyTtsVoiceOption> VoicesResult { get; set; } = [];

        public Exception? HealthException { get; set; }

        public Exception? VoicesException { get; set; }

        public Task<FeishuReplyTtsHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            HealthCallCount++;
            if (HealthException is not null)
            {
                throw HealthException;
            }

            return Task.FromResult(HealthResult);
        }

        public Task<IReadOnlyList<FeishuReplyTtsVoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default)
        {
            if (VoicesException is not null)
            {
                throw VoicesException;
            }

            return Task.FromResult(VoicesResult);
        }

        public Task<Stream> SynthesizeAsync(string text, string voiceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeReplyTtsHostEnvironment : IReplyTtsHostEnvironment
    {
        private readonly IReadOnlyList<ReplyTtsDriveDescriptor> _drives;

        public FakeReplyTtsHostEnvironment(
            bool isWindows,
            string? systemDriveRoot,
            IReadOnlyList<ReplyTtsDriveDescriptor> drives)
        {
            IsWindows = isWindows;
            SystemDriveRoot = systemDriveRoot;
            _drives = drives;
        }

        public bool IsWindows { get; }

        public string? SystemDriveRoot { get; }

        public IReadOnlyList<ReplyTtsDriveDescriptor> GetFixedDrives()
        {
            return _drives;
        }
    }

    private sealed class MutableOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; private set; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
