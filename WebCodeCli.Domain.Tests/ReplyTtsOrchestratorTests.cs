using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Channels;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Tests;

public sealed class ReplyTtsOrchestratorTests
{
    [Fact]
    public async Task QueueCompletedReplyAsync_SkipsWhenReplyTtsDisabled()
    {
        using var harness = new ReplyTtsOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                ReplyTtsEnabled = false
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyTtsRequest
        {
            ChatId = "oc-disabled-chat",
            Username = "luhaiyan",
            Output = "需要播报"
        });

        await WaitUntilAsync(() => harness.ConfigService.UsernameLookupCount == 1);

        Assert.Empty(harness.MeloClient.Calls);
        Assert.Empty(harness.TranscodeService.Calls);
        Assert.Empty(harness.AudioService.Calls);
        Assert.Equal(0, harness.CardKit.SendTextCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# **__**")]
    public async Task QueueCompletedReplyAsync_SkipsWhenNormalizedOutputIsEmpty(string output)
    {
        using var harness = new ReplyTtsOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                ReplyTtsEnabled = true
            });

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyTtsRequest
        {
            ChatId = "oc-empty-chat",
            Username = "luhaiyan",
            Output = output
        });

        await WaitUntilAsync(() => harness.ConfigService.UsernameLookupCount == 1);

        Assert.Empty(harness.MeloClient.Calls);
        Assert.Empty(harness.TranscodeService.Calls);
        Assert.Empty(harness.AudioService.Calls);
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_FallsBackToDefaultVoiceAndProcessesChunksInOrder()
    {
        using var harness = new ReplyTtsOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                ReplyTtsEnabled = true,
                ReplyTtsVoiceId = "missing-voice"
            },
            chunkMaxChars: 8);

        harness.PlatformService.Resolution = new FeishuReplyTtsVoiceResolutionResult
        {
            Success = true,
            VoiceId = "platform-default",
            UsedFallback = true,
            Voice = new FeishuReplyTtsVoiceOption
            {
                VoiceId = "platform-default",
                DisplayName = "Platform Default"
            }
        };

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyTtsRequest
        {
            ChatId = "oc-order-chat",
            Username = "luhaiyan",
            Output = "第一段。\n\n第二段。"
        });

        await WaitUntilAsync(() => harness.AudioService.Calls.Count == 2);
        await WaitUntilAsync(() => harness.JobTempRoot is not null && (!Directory.Exists(harness.JobTempRoot) || !Directory.EnumerateDirectories(harness.JobTempRoot).Any()));

        Assert.Collection(
            harness.Sequence,
            item => Assert.Equal("synthesize:1:platform-default:第一段。", item),
            item => Assert.Equal("transcode:1", item),
            item => Assert.Equal("send:1", item),
            item => Assert.Equal("synthesize:2:platform-default:第二段。", item),
            item => Assert.Equal("transcode:2", item),
            item => Assert.Equal("send:2", item));

        Assert.All(harness.MeloClient.Calls, call => Assert.Equal("platform-default", call.VoiceId));
        Assert.All(harness.AudioService.Calls, call => Assert.True(call.DurationMs > 0));
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_StopsAfterFailedChunkAndSendsOneFailureNotice()
    {
        using var harness = new ReplyTtsOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                ReplyTtsEnabled = true
            },
            chunkMaxChars: 8);

        harness.TranscodeService.FailChunkIndex = 2;

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyTtsRequest
        {
            ChatId = "oc-failure-chat",
            Username = "luhaiyan",
            Output = "第一段。\n\n第二段。\n\n第三段。"
        });

        await WaitUntilAsync(() => harness.CardKit.SendTextCallCount == 1);

        Assert.Equal(2, harness.MeloClient.Calls.Count);
        Assert.Equal(2, harness.TranscodeService.Calls.Count);
        Assert.Single(harness.AudioService.Calls);
        Assert.Equal(1, harness.CardKit.SendTextCallCount);
        Assert.DoesNotContain(harness.Sequence, item => item == "synthesize:3:default-voice:第三段。");
    }

    [Fact]
    public async Task QueueCompletedReplyAsync_SerializesJobsPerChat()
    {
        using var harness = new ReplyTtsOrchestratorHarness(
            new UserFeishuBotConfigEntity
            {
                Username = "luhaiyan",
                ReplyTtsEnabled = true
            });

        harness.AudioService.BlockFirstSend = true;

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyTtsRequest
        {
            ChatId = "oc-serialized-chat",
            Username = "luhaiyan",
            Output = "第一条"
        });

        await harness.AudioService.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await harness.Orchestrator.QueueCompletedReplyAsync(new FeishuCompletedReplyTtsRequest
        {
            ChatId = "oc-serialized-chat",
            Username = "luhaiyan",
            Output = "第二条"
        });

        await Task.Delay(150);

        Assert.Single(harness.MeloClient.Calls);

        harness.AudioService.ReleaseFirstSend();

        await WaitUntilAsync(() => harness.AudioService.Calls.Count == 2);

        Assert.Collection(
            harness.MeloClient.Calls,
            first => Assert.Equal("第一条", first.Text),
            second => Assert.Equal("第二条", second.Text));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "Timed out waiting for the expected condition.");
    }

    private sealed class ReplyTtsOrchestratorHarness : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        public ReplyTtsOrchestratorHarness(UserFeishuBotConfigEntity config, int chunkMaxChars = 1200)
        {
            TempRoot = Path.Combine(Path.GetTempPath(), $"reply-tts-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(TempRoot);

            ConfigService = new TrackingUserFeishuBotConfigService(config);
            PlatformService = new TrackingReplyTtsPlatformService();
            MeloClient = new TrackingMeloTtsClient(Sequence);
            TranscodeService = new TrackingAudioTranscodeService(Sequence);
            AudioService = new TrackingFeishuAudioMessageService(Sequence);
            CardKit = new TrackingFeishuCardKitClient();

            var services = new ServiceCollection();
            services.AddSingleton(new ReplyTtsStorageRootResolver(
                new StaticOptionsMonitor<FeishuReplyTtsOptions>(new FeishuReplyTtsOptions
                {
                    TtsStorageRoot = TempRoot
                })));
            services.AddSingleton<ReplyTtsSpeechTextNormalizer>();
            services.AddScoped(_ => new ReplyTtsChunker(chunkMaxChars));
            services.AddScoped<IFeishuReplyTtsPlatformService>(_ => PlatformService);
            services.AddScoped<IMeloTtsClient>(_ => MeloClient);
            services.AddScoped<IAudioTranscodeService>(_ => TranscodeService);
            services.AddScoped<IFeishuAudioMessageService>(_ => AudioService);
            services.AddScoped<IUserFeishuBotConfigService>(_ => ConfigService);
            services.AddScoped<IFeishuCardKitClient>(_ => CardKit);
            services.AddLogging();

            _serviceProvider = services.BuildServiceProvider();
            Orchestrator = new ReplyTtsOrchestrator(
                _serviceProvider,
                _serviceProvider.GetRequiredService<ReplyTtsStorageRootResolver>(),
                NullLogger<ReplyTtsOrchestrator>.Instance);
        }

        public ConcurrentQueue<string> Sequence { get; } = new();

        public string TempRoot { get; }

        public string JobTempRoot => Path.Combine(TempRoot, "temp");

        public TrackingUserFeishuBotConfigService ConfigService { get; }

        public TrackingReplyTtsPlatformService PlatformService { get; }

        public TrackingMeloTtsClient MeloClient { get; }

        public TrackingAudioTranscodeService TranscodeService { get; }

        public TrackingFeishuAudioMessageService AudioService { get; }

        public TrackingFeishuCardKitClient CardKit { get; }

        public ReplyTtsOrchestrator Orchestrator { get; }

        public void Dispose()
        {
            _serviceProvider.Dispose();
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
    }

    private sealed class TrackingUserFeishuBotConfigService(UserFeishuBotConfigEntity config) : IUserFeishuBotConfigService
    {
        public int UsernameLookupCount { get; private set; }

        public Task<UserFeishuBotConfigEntity?> GetByUsernameAsync(string username)
        {
            UsernameLookupCount++;
            return Task.FromResult<UserFeishuBotConfigEntity?>(string.Equals(username, config.Username, StringComparison.OrdinalIgnoreCase)
                ? config
                : null);
        }

        public Task<UserFeishuBotConfigEntity?> GetByAppIdAsync(string appId)
            => Task.FromResult<UserFeishuBotConfigEntity?>(null);

        public Task<UserFeishuBotConfigSaveResult> SaveAsync(UserFeishuBotConfigEntity configEntity)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string username) => Task.FromResult(true);

        public Task<string?> FindConflictingUsernameByAppIdAsync(string username, string? appId)
            => Task.FromResult<string?>(null);

        public Task<List<UserFeishuBotConfigEntity>> GetAutoStartCandidatesAsync()
            => Task.FromResult(new List<UserFeishuBotConfigEntity>());

        public Task<bool> UpdateRuntimePreferenceAsync(string username, bool autoStartEnabled, DateTime? lastStartedAt = null)
            => Task.FromResult(true);

        public FeishuOptions GetSharedDefaults() => new()
        {
            Enabled = true,
            AppId = "shared-app-id",
            AppSecret = "shared-secret"
        };

        public Task<FeishuOptions> GetEffectiveOptionsAsync(string? username) => Task.FromResult(GetSharedDefaults());

        public Task<FeishuOptions?> GetEffectiveOptionsByAppIdAsync(string? appId)
            => Task.FromResult<FeishuOptions?>(null);
    }

    private sealed class TrackingReplyTtsPlatformService : IFeishuReplyTtsPlatformService
    {
        public FeishuReplyTtsVoiceResolutionResult Resolution { get; set; } = new()
        {
            Success = true,
            VoiceId = "default-voice",
            Voice = new FeishuReplyTtsVoiceOption
            {
                VoiceId = "default-voice",
                DisplayName = "Default Voice"
            }
        };

        public Task<FeishuReplyTtsHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<FeishuReplyTtsVoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FeishuReplyTtsVoiceResolutionResult> ResolveVoiceOrFallbackAsync(string? savedVoiceId, CancellationToken cancellationToken = default)
            => Task.FromResult(Resolution);
    }

    private sealed class TrackingMeloTtsClient(ConcurrentQueue<string> sequence) : IMeloTtsClient
    {
        public List<SynthesizeCall> Calls { get; } = new();

        public Task<FeishuReplyTtsHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<FeishuReplyTtsVoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream> SynthesizeAsync(string text, string voiceId, CancellationToken cancellationToken = default)
        {
            var call = new SynthesizeCall(text, voiceId);
            Calls.Add(call);
            sequence.Enqueue($"synthesize:{Calls.Count}:{voiceId}:{text}");
            return Task.FromResult<Stream>(new MemoryStream(CreateWaveBytes(durationMs: 1000)));
        }
    }

    private sealed class TrackingAudioTranscodeService(ConcurrentQueue<string> sequence) : IAudioTranscodeService
    {
        public int? FailChunkIndex { get; set; }

        public List<TranscodeCall> Calls { get; } = new();

        public Task<string> TranscodeChunkAsync(string jobId, string inputWavPath, int chunkIndex, CancellationToken cancellationToken = default)
        {
            Calls.Add(new TranscodeCall(jobId, inputWavPath, chunkIndex));
            sequence.Enqueue($"transcode:{chunkIndex}");

            if (FailChunkIndex == chunkIndex)
            {
                throw new InvalidOperationException($"chunk {chunkIndex} failed");
            }

            var outputPath = Path.Combine(Path.GetDirectoryName(inputWavPath)!, $"chunk-{chunkIndex:000}.opus");
            File.WriteAllBytes(outputPath, [1, 2, 3, 4]);
            return Task.FromResult(outputPath);
        }
    }

    private sealed class TrackingFeishuAudioMessageService(ConcurrentQueue<string> sequence) : IFeishuAudioMessageService
    {
        private readonly TaskCompletionSource<bool> _releaseFirstSend = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockFirstSend { get; set; }

        public TaskCompletionSource<bool> FirstSendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<AudioSendCall> Calls { get; } = new();

        public async Task<string> SendAudioMessageAsync(
            string chatId,
            string filePath,
            int durationMs,
            string? username = null,
            string? appId = null,
            CancellationToken cancellationToken = default)
        {
            var call = new AudioSendCall(chatId, filePath, durationMs, username, appId);
            Calls.Add(call);
            sequence.Enqueue($"send:{Calls.Count}");

            if (BlockFirstSend && Calls.Count == 1)
            {
                FirstSendStarted.TrySetResult(true);
                await _releaseFirstSend.Task.WaitAsync(cancellationToken);
            }

            return $"audio-{Calls.Count}";
        }

        public void ReleaseFirstSend()
        {
            _releaseFirstSend.TrySetResult(true);
        }
    }

    private sealed class TrackingFeishuCardKitClient : IFeishuCardKitClient
    {
        public int SendTextCallCount { get; private set; }

        public List<string> TextMessages { get; } = new();

        public Task<string> CreateCardAsync(string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<bool> UpdateCardAsync(string cardId, string content, int sequence, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> SendCardMessageAsync(string chatId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> SendTextMessageAsync(string chatId, string content, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
        {
            SendTextCallCount++;
            TextMessages.Add(content);
            return Task.FromResult($"text-{SendTextCallCount}");
        }

        public Task<string> ReplyCardMessageAsync(string replyMessageId, string cardId, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> ReplyTextMessageAsync(string replyMessageId, string content, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> UploadAudioFileAsync(string filePath, int durationMs, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> SendAudioMessageAsync(string chatId, string fileKey, int durationMs, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<FeishuStreamingHandle> CreateStreamingHandleAsync(string chatId, string? replyMessageId, string initialContent, string? title = null, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null, FeishuStreamingCardChrome? chrome = null)
            => throw new NotSupportedException();

        public Task<string> SendRawCardAsync(string chatId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> ReplyElementsCardAsync(string replyMessageId, FeishuNetSdk.Im.Dtos.ElementsCardV2Dto card, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();

        public Task<string> ReplyRawCardAsync(string replyMessageId, string cardJson, CancellationToken cancellationToken = default, FeishuOptions? optionsOverride = null)
            => throw new NotSupportedException();
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed record SynthesizeCall(string Text, string VoiceId);

    private sealed record TranscodeCall(string JobId, string InputWavPath, int ChunkIndex);

    private sealed record AudioSendCall(string ChatId, string FilePath, int DurationMs, string? Username, string? AppId);

    private static byte[] CreateWaveBytes(int durationMs)
    {
        const short channels = 1;
        const int sampleRate = 16000;
        const short bitsPerSample = 16;
        var bytesPerSample = bitsPerSample / 8;
        var sampleCount = sampleRate * durationMs / 1000;
        var dataSize = sampleCount * channels * bytesPerSample;
        var byteRate = sampleRate * channels * bytesPerSample;
        var blockAlign = (short)(channels * bytesPerSample);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        writer.Flush();
        return stream.ToArray();
    }
}
