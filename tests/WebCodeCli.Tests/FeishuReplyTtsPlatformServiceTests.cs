using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Channels;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class FeishuReplyTtsPlatformServiceTests
{
    [Fact]
    public async Task ResolveVoiceOrFallbackAsync_WhenHealthProbeFails_RestartsServiceAndResolvesVoice()
    {
        var ttsClient = new StubSherpaKokoroTtsClient();
        ttsClient.QueueHealthException(new HttpRequestException("connection refused"));
        ttsClient.QueueHealthResult(CreateServiceHealth(isAvailable: true));
        ttsClient.QueueVoicesResult(
        [
            new FeishuReplyTtsVoiceOption
            {
                VoiceId = "zh_47",
                DisplayName = "Chinese 47"
            }
        ]);

        var localServiceManager = new StubReplyTtsLocalServiceManager
        {
            EnsureStartedResult = CreateServiceHealth(isAvailable: true)
        };

        var service = CreateService(ttsClient, localServiceManager);

        var result = await service.ResolveVoiceOrFallbackAsync(savedVoiceId: null);

        Assert.True(result.Success);
        Assert.Equal("zh_47", result.VoiceId);
        Assert.Equal(1, localServiceManager.EnsureStartedCallCount);
        Assert.Equal(1, ttsClient.GetHealthCallCount);
        Assert.Equal(1, ttsClient.GetVoicesCallCount);
    }

    [Fact]
    public async Task ResolveVoiceOrFallbackAsync_WhenVoicesFetchFailsAfterHealthyProbe_RestartsServiceAndResolvesVoice()
    {
        var ttsClient = new StubSherpaKokoroTtsClient();
        ttsClient.QueueHealthResult(CreateServiceHealth(isAvailable: true));
        ttsClient.QueueVoicesException(new HttpRequestException("connection refused"));
        ttsClient.QueueHealthResult(CreateServiceHealth(isAvailable: true));
        ttsClient.QueueVoicesResult(
        [
            new FeishuReplyTtsVoiceOption
            {
                VoiceId = "zh_47",
                DisplayName = "Chinese 47"
            }
        ]);

        var localServiceManager = new StubReplyTtsLocalServiceManager
        {
            EnsureStartedResult = CreateServiceHealth(isAvailable: true)
        };

        var service = CreateService(ttsClient, localServiceManager);

        var result = await service.ResolveVoiceOrFallbackAsync(savedVoiceId: null);

        Assert.True(result.Success);
        Assert.Equal("zh_47", result.VoiceId);
        Assert.Equal(1, localServiceManager.EnsureStartedCallCount);
        Assert.Equal(1, ttsClient.GetHealthCallCount);
        Assert.Equal(2, ttsClient.GetVoicesCallCount);
    }

    private static FeishuReplyTtsPlatformService CreateService(
        StubSherpaKokoroTtsClient ttsClient,
        StubReplyTtsLocalServiceManager localServiceManager)
    {
        var options = new FeishuReplyTtsOptions
        {
            TtsStorageRoot = "/data/webcode/kokoro",
            FfmpegExecutablePath = "ffmpeg",
            TtsDefaultVoiceId = "zh_47"
        };

        var storageRootResolver = new ReplyTtsStorageRootResolver(
            new StubOptionsMonitor<FeishuReplyTtsOptions>(options),
            new StubReplyTtsHostEnvironment());

        return new FeishuReplyTtsPlatformService(
            storageRootResolver,
            Options.Create(options),
            ttsClient,
            localServiceManager);
    }

    private static FeishuReplyTtsHealthStatus CreateServiceHealth(bool isAvailable)
    {
        return new FeishuReplyTtsHealthStatus
        {
            IsAvailable = isAvailable,
            ServiceStatus = isAvailable ? "ok" : "unreachable",
            Message = isAvailable ? "healthy" : "unreachable",
            DefaultVoiceId = "zh_47",
            Device = "cpu"
        };
    }

    private sealed class StubSherpaKokoroTtsClient : ISherpaKokoroTtsClient
    {
        private readonly Queue<Func<FeishuReplyTtsHealthStatus>> _healthResponses = new();
        private readonly Queue<Func<IReadOnlyList<FeishuReplyTtsVoiceOption>>> _voiceResponses = new();

        public int GetHealthCallCount { get; private set; }

        public int GetVoicesCallCount { get; private set; }

        public void QueueHealthResult(FeishuReplyTtsHealthStatus result)
        {
            _healthResponses.Enqueue(() => result);
        }

        public void QueueHealthException(Exception exception)
        {
            _healthResponses.Enqueue(() => throw exception);
        }

        public void QueueVoicesResult(IReadOnlyList<FeishuReplyTtsVoiceOption> voices)
        {
            _voiceResponses.Enqueue(() => voices);
        }

        public void QueueVoicesException(Exception exception)
        {
            _voiceResponses.Enqueue(() => throw exception);
        }

        public Task<FeishuReplyTtsHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            GetHealthCallCount++;
            return Task.FromResult(_healthResponses.Dequeue().Invoke());
        }

        public Task<IReadOnlyList<FeishuReplyTtsVoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default)
        {
            GetVoicesCallCount++;
            return Task.FromResult(_voiceResponses.Dequeue().Invoke());
        }

        public Task<Stream> SynthesizeAsync(string text, string voiceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubReplyTtsLocalServiceManager : IReplyTtsLocalServiceManager
    {
        public FeishuReplyTtsHealthStatus EnsureStartedResult { get; set; } = new();

        public int EnsureStartedCallCount { get; private set; }

        public Task<FeishuReplyTtsHealthStatus> EnsureStartedAsync(
            FeishuReplyTtsHealthStatus storageHealth,
            CancellationToken cancellationToken = default)
        {
            EnsureStartedCallCount++;
            return Task.FromResult(EnsureStartedResult);
        }
    }

    private sealed class StubOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StubOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable? OnChange(Action<T, string?> listener)
        {
            return null;
        }
    }

    private sealed class StubReplyTtsHostEnvironment : IReplyTtsHostEnvironment
    {
        public bool IsWindows => false;

        public string? SystemDriveRoot => null;

        public IReadOnlyList<ReplyTtsDriveDescriptor> GetFixedDrives()
        {
            return [];
        }

        public bool DirectoryExists(string path)
        {
            return false;
        }

        public bool FileExists(string path)
        {
            return false;
        }
    }
}
