using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public sealed class AudioTranscodeServiceTests : IDisposable
{
    private readonly string _sandboxRoot = Path.Combine(Path.GetTempPath(), "webcode-audio-transcode-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TranscodeChunkAsync_WhenFfmpegPathIsMissing_Throws()
    {
        Directory.CreateDirectory(_sandboxRoot);
        var service = CreateService(
            new FeishuReplyTtsOptions
            {
                TtsStorageRoot = Path.Combine(_sandboxRoot, "reply-tts")
            },
            new RecordingExternalProcessRunner());
        var inputPath = CreateInputFile();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TranscodeChunkAsync("job-1", inputPath, chunkIndex: 1, TestContext.Current.CancellationToken));

        Assert.Contains("FfmpegExecutablePath", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranscodeChunkAsync_WritesUnderResolvedTempRoot_AndInvokesExpectedFfmpegArguments()
    {
        Directory.CreateDirectory(_sandboxRoot);
        var runner = new RecordingExternalProcessRunner();
        var ffmpegPath = Path.Combine(_sandboxRoot, "ffmpeg.exe");
        File.WriteAllText(ffmpegPath, "stub");

        var service = CreateService(
            new FeishuReplyTtsOptions
            {
                TtsStorageRoot = Path.Combine(_sandboxRoot, "reply-tts"),
                FfmpegExecutablePath = ffmpegPath
            },
            runner);
        var inputPath = CreateInputFile();

        var outputPath = await service.TranscodeChunkAsync("job-42", inputPath, chunkIndex: 1, TestContext.Current.CancellationToken);

        Assert.Equal(ffmpegPath, runner.FileName);
        Assert.Contains("-y", runner.Arguments, StringComparison.Ordinal);
        Assert.Contains("-i", runner.Arguments, StringComparison.Ordinal);
        Assert.Contains("libopus", runner.Arguments, StringComparison.Ordinal);
        Assert.Contains("-ac 1", runner.Arguments, StringComparison.Ordinal);
        Assert.Contains("-ar 16000", runner.Arguments, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(_sandboxRoot, "reply-tts", "temp", "job-42"), runner.WorkingDirectory);
        Assert.Equal(Path.Combine(_sandboxRoot, "reply-tts", "temp", "job-42", "chunk-001.opus"), outputPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandboxRoot))
            {
                Directory.Delete(_sandboxRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private AudioTranscodeService CreateService(FeishuReplyTtsOptions options, RecordingExternalProcessRunner runner)
    {
        return new AudioTranscodeService(
            Options.Create(options),
            new ReplyTtsStorageRootResolver(
                new MutableOptionsMonitor<FeishuReplyTtsOptions>(options),
                new FakeReplyTtsHostEnvironment(isWindows: false, systemDriveRoot: null, drives: [])),
            runner);
    }

    private string CreateInputFile()
    {
        var inputPath = Path.Combine(_sandboxRoot, "input.wav");
        File.WriteAllText(inputPath, "wav");
        return inputPath;
    }

    private sealed class RecordingExternalProcessRunner : IExternalProcessRunner
    {
        public string? FileName { get; private set; }

        public string? Arguments { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public Task<ExternalProcessResult> RunAsync(
            string fileName,
            string arguments,
            string? workingDirectory = null,
            CancellationToken cancellationToken = default)
        {
            FileName = fileName;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            return Task.FromResult(new ExternalProcessResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class MutableOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; private set; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class FakeReplyTtsHostEnvironment(
        bool isWindows,
        string? systemDriveRoot,
        IReadOnlyList<ReplyTtsDriveDescriptor> drives) : IReplyTtsHostEnvironment
    {
        public bool IsWindows { get; } = isWindows;

        public string? SystemDriveRoot { get; } = systemDriveRoot;

        public IReadOnlyList<ReplyTtsDriveDescriptor> GetFixedDrives() => drives;
    }
}
