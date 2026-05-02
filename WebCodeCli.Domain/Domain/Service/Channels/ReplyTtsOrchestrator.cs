using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;

namespace WebCodeCli.Domain.Domain.Service.Channels;

[ServiceDescription(typeof(IReplyTtsOrchestrator), ServiceLifetime.Singleton)]
public sealed class ReplyTtsOrchestrator : IReplyTtsOrchestrator
{
    private const string FailureNotice = "回复语音发送失败，已停止后续音频。";

    private readonly IServiceProvider _serviceProvider;
    private readonly ReplyTtsStorageRootResolver _storageRootResolver;
    private readonly ILogger<ReplyTtsOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _chatLocks = new(StringComparer.OrdinalIgnoreCase);

    public ReplyTtsOrchestrator(
        IServiceProvider serviceProvider,
        ReplyTtsStorageRootResolver storageRootResolver,
        ILogger<ReplyTtsOrchestrator> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _storageRootResolver = storageRootResolver ?? throw new ArgumentNullException(nameof(storageRootResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task QueueCompletedReplyAsync(FeishuCompletedReplyTtsRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ChatId))
        {
            throw new ArgumentException("Chat ID is required.", nameof(request));
        }

        _ = Task.Run(() => ProcessQueuedReplyAsync(request));
        return Task.CompletedTask;
    }

    private async Task ProcessQueuedReplyAsync(FeishuCompletedReplyTtsRequest request)
    {
        var chatLock = _chatLocks.GetOrAdd(request.ChatId.Trim(), static _ => new SemaphoreSlim(1, 1));
        await chatLock.WaitAsync();
        try
        {
            await ProcessReplyCoreAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reply TTS orchestration failed for chat {ChatId}", request.ChatId);
        }
        finally
        {
            chatLock.Release();
        }
    }

    private async Task ProcessReplyCoreAsync(FeishuCompletedReplyTtsRequest request)
    {
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IUserFeishuBotConfigService>();
        var userConfig = await ResolveBotConfigAsync(configService, request.Username, request.AppId);
        if (userConfig?.ReplyTtsEnabled != true)
        {
            return;
        }

        var normalizer = scope.ServiceProvider.GetRequiredService<ReplyTtsSpeechTextNormalizer>();
        var normalizedOutput = normalizer.Normalize(request.Output);
        if (string.IsNullOrWhiteSpace(normalizedOutput))
        {
            return;
        }

        var storageHealth = _storageRootResolver.Resolve();
        if (!storageHealth.IsAvailable || string.IsNullOrWhiteSpace(storageHealth.TempRoot))
        {
            _logger.LogWarning(
                "Skipping reply TTS for chat {ChatId} because temp storage is unavailable: {Message}",
                request.ChatId,
                storageHealth.Message);
            return;
        }

        var platformService = scope.ServiceProvider.GetRequiredService<IFeishuReplyTtsPlatformService>();
        var voiceResolution = await platformService.ResolveVoiceOrFallbackAsync(userConfig.ReplyTtsVoiceId);
        if (!voiceResolution.Success || string.IsNullOrWhiteSpace(voiceResolution.VoiceId))
        {
            _logger.LogWarning(
                "Skipping reply TTS for chat {ChatId} because voice resolution failed: {Message}",
                request.ChatId,
                voiceResolution.Message);
            return;
        }

        var chunker = scope.ServiceProvider.GetRequiredService<ReplyTtsChunker>();
        var chunks = chunker.Split(normalizedOutput);
        if (chunks.Count == 0)
        {
            return;
        }

        var meloTtsClient = scope.ServiceProvider.GetRequiredService<IMeloTtsClient>();
        var audioTranscodeService = scope.ServiceProvider.GetRequiredService<IAudioTranscodeService>();
        var audioMessageService = scope.ServiceProvider.GetRequiredService<IFeishuAudioMessageService>();
        var cardKitClient = scope.ServiceProvider.GetRequiredService<IFeishuCardKitClient>();

        var jobId = CreateJobId();
        var jobDirectory = Path.Combine(storageHealth.TempRoot, jobId);
        Directory.CreateDirectory(jobDirectory);

        var success = false;
        try
        {
            for (var index = 0; index < chunks.Count; index++)
            {
                var chunkIndex = index + 1;
                var chunkText = chunks[index];

                try
                {
                    await using var wavStream = await meloTtsClient.SynthesizeAsync(chunkText, voiceResolution.VoiceId);
                    var wavPath = Path.Combine(jobDirectory, $"chunk-{chunkIndex:000}.wav");
                    await WriteStreamToFileAsync(wavStream, wavPath);

                    var durationMs = GetWaveDurationMs(wavPath);
                    var opusPath = await audioTranscodeService.TranscodeChunkAsync(jobId, wavPath, chunkIndex);
                    await audioMessageService.SendAudioMessageAsync(
                        request.ChatId,
                        opusPath,
                        durationMs,
                        request.Username,
                        request.AppId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Reply TTS chunk {ChunkIndex} failed for chat {ChatId}; remaining chunks will be skipped.",
                        chunkIndex,
                        request.ChatId);

                    await SendFailureNoticeAsync(cardKitClient, configService, request);
                    return;
                }
            }

            success = true;
        }
        finally
        {
            if (success)
            {
                TryDeleteDirectory(jobDirectory);
            }
        }
    }

    private async Task SendFailureNoticeAsync(
        IFeishuCardKitClient cardKitClient,
        IUserFeishuBotConfigService configService,
        FeishuCompletedReplyTtsRequest request)
    {
        try
        {
            var options = await ResolveEffectiveOptionsAsync(configService, request.Username, request.AppId);
            await cardKitClient.SendTextMessageAsync(request.ChatId, FailureNotice, optionsOverride: options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send reply TTS failure notice for chat {ChatId}", request.ChatId);
        }
    }

    private static async Task WriteStreamToFileAsync(Stream input, string outputPath)
    {
        input.Position = 0;
        await using var output = File.Create(outputPath);
        await input.CopyToAsync(output);
    }

    private static int GetWaveDurationMs(string wavPath)
    {
        using var stream = File.OpenRead(wavPath);
        using var reader = new BinaryReader(stream);

        if (!IsChunk(reader, "RIFF"))
        {
            throw new InvalidOperationException("Reply TTS synthesis did not produce a valid RIFF WAV file.");
        }

        _ = reader.ReadInt32();

        if (!IsChunk(reader, "WAVE"))
        {
            throw new InvalidOperationException("Reply TTS synthesis did not produce a valid WAVE file.");
        }

        int? byteRate = null;
        int? dataSize = null;

        while (stream.Position <= stream.Length - 8)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            if (chunkSize < 0)
            {
                throw new InvalidOperationException("Reply TTS synthesis produced an invalid WAV chunk length.");
            }

            switch (chunkId)
            {
                case "fmt ":
                    if (chunkSize < 16)
                    {
                        throw new InvalidOperationException("Reply TTS synthesis produced an invalid WAV format chunk.");
                    }

                    _ = reader.ReadInt16();
                    _ = reader.ReadInt16();
                    _ = reader.ReadInt32();
                    byteRate = reader.ReadInt32();
                    _ = reader.ReadInt16();
                    _ = reader.ReadInt16();
                    SkipRemainingChunkBytes(stream, chunkSize - 16);
                    break;

                case "data":
                    dataSize = chunkSize;
                    SkipRemainingChunkBytes(stream, chunkSize);
                    break;

                default:
                    SkipRemainingChunkBytes(stream, chunkSize);
                    break;
            }

            if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
            {
                stream.Position++;
            }

            if (byteRate.HasValue && dataSize.HasValue)
            {
                break;
            }
        }

        if (!byteRate.HasValue || !dataSize.HasValue || byteRate.Value <= 0)
        {
            throw new InvalidOperationException("Reply TTS synthesis produced a WAV file without duration metadata.");
        }

        return Math.Max(1, (int)Math.Ceiling(dataSize.Value * 1000d / byteRate.Value));
    }

    private static bool IsChunk(BinaryReader reader, string expected)
    {
        return string.Equals(new string(reader.ReadChars(4)), expected, StringComparison.Ordinal);
    }

    private static void SkipRemainingChunkBytes(Stream stream, int count)
    {
        if (count <= 0)
        {
            return;
        }

        stream.Position += count;
    }

    private static string CreateJobId()
    {
        return $"reply-tts-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
    }

    private static void TryDeleteDirectory(string jobDirectory)
    {
        try
        {
            if (Directory.Exists(jobDirectory))
            {
                Directory.Delete(jobDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static async Task<UserFeishuBotConfigEntity?> ResolveBotConfigAsync(
        IUserFeishuBotConfigService configService,
        string? username,
        string? appId)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            return await configService.GetByUsernameAsync(username.Trim());
        }

        if (!string.IsNullOrWhiteSpace(appId))
        {
            return await configService.GetByAppIdAsync(appId.Trim());
        }

        return null;
    }

    private static async Task<FeishuOptions> ResolveEffectiveOptionsAsync(
        IUserFeishuBotConfigService configService,
        string? username,
        string? appId)
    {
        if (!string.IsNullOrWhiteSpace(appId))
        {
            var appOptions = await configService.GetEffectiveOptionsByAppIdAsync(appId.Trim());
            if (appOptions != null)
            {
                return appOptions;
            }
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            return await configService.GetEffectiveOptionsAsync(username.Trim());
        }

        return configService.GetSharedDefaults();
    }
}
