using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;

namespace WebCodeCli.Domain.Domain.Service.Channels;

[ServiceDescription(typeof(IAudioTranscodeService), ServiceLifetime.Scoped)]
public sealed class AudioTranscodeService : IAudioTranscodeService
{
    private readonly FeishuReplyTtsOptions _options;
    private readonly ReplyTtsStorageRootResolver _storageRootResolver;
    private readonly IExternalProcessRunner _externalProcessRunner;

    public AudioTranscodeService(
        IOptions<FeishuReplyTtsOptions> options,
        ReplyTtsStorageRootResolver storageRootResolver,
        IExternalProcessRunner externalProcessRunner)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _storageRootResolver = storageRootResolver ?? throw new ArgumentNullException(nameof(storageRootResolver));
        _externalProcessRunner = externalProcessRunner ?? throw new ArgumentNullException(nameof(externalProcessRunner));
    }

    public async Task<string> TranscodeChunkAsync(
        string jobId,
        string inputWavPath,
        int chunkIndex,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FfmpegExecutablePath))
        {
            throw new InvalidOperationException("Feishu reply TTS ffmpeg configuration is missing. Set FeishuReplyTts:FfmpegExecutablePath.");
        }

        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(inputWavPath))
        {
            throw new ArgumentException("Input WAV path is required.", nameof(inputWavPath));
        }

        if (chunkIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Chunk index must be greater than zero.");
        }

        var health = _storageRootResolver.Resolve();
        if (!health.IsAvailable || string.IsNullOrWhiteSpace(health.TempRoot))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(health.Message)
                    ? "Feishu reply TTS temp storage is unavailable."
                    : health.Message);
        }

        var jobDirectory = Path.Combine(health.TempRoot, SanitizePathSegment(jobId));
        Directory.CreateDirectory(jobDirectory);

        var outputPath = Path.Combine(jobDirectory, $"chunk-{chunkIndex:000}.opus");
        var arguments = string.Join(
            ' ',
            "-y",
            "-i",
            Quote(inputWavPath),
            "-acodec",
            "libopus",
            "-ac",
            "1",
            "-ar",
            "16000",
            Quote(outputPath));

        var result = await _externalProcessRunner.RunAsync(
            _options.FfmpegExecutablePath.Trim(),
            arguments,
            jobDirectory,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg transcode failed with exit code {result.ExitCode}: {result.StandardError}".Trim());
        }

        return outputPath;
    }

    private static string Quote(string path)
    {
        return $"\"{path}\"";
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "reply-tts-job" : sanitized;
    }
}
