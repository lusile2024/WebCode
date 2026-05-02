namespace WebCodeCli.Domain.Common.Options;

public sealed class FeishuReplyTtsOptions
{
    public string? TtsStorageRoot { get; set; }

    public bool AllowSystemDriveInstall { get; set; }

    public string TtsServiceBaseUrl { get; set; } = "http://127.0.0.1:5057";

    public int TtsServiceTimeoutSeconds { get; set; } = 60;

    public string TtsPreferredDevice { get; set; } = "gpu-auto";

    public string? TtsDefaultVoiceId { get; set; }

    public int TtsChunkMaxChars { get; set; } = 1200;

    public string? FfmpegExecutablePath { get; set; }
}
