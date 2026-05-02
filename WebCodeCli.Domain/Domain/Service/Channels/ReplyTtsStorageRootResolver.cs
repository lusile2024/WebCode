using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

public sealed class ReplyTtsStorageRootResolver
{
    private const string NonWindowsDefaultRoot = "/data/webcode/melotts";

    private readonly FeishuReplyTtsOptions _options;
    private readonly IReplyTtsHostEnvironment _hostEnvironment;

    public ReplyTtsStorageRootResolver(
        IOptions<FeishuReplyTtsOptions> options,
        IReplyTtsHostEnvironment? hostEnvironment = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _hostEnvironment = hostEnvironment ?? new SystemReplyTtsHostEnvironment();
    }

    public FeishuReplyTtsHealthStatus Resolve()
    {
        var explicitRoot = _options.TtsStorageRoot?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            var useWindowsPaths = UsesWindowsSeparators(explicitRoot) || _hostEnvironment.IsWindows;
            return CreateAvailable(
                NormalizeStorageRoot(explicitRoot, useWindowsPaths),
                "Using configured Feishu reply TTS storage root.",
                useWindowsPaths);
        }

        if (!_hostEnvironment.IsWindows)
        {
            return CreateAvailable(
                NonWindowsDefaultRoot,
                "Using default non-Windows Feishu reply TTS storage root.",
                useWindowsPaths: false);
        }

        var systemDriveRoot = NormalizeDriveRoot(_hostEnvironment.SystemDriveRoot);
        var writableDrives = _hostEnvironment.GetFixedDrives()
            .Where(d => d.IsReady && d.IsWritable)
            .ToList();

        var nonSystemDrive = writableDrives.FirstOrDefault(d => !IsSameDrive(d.RootPath, systemDriveRoot));
        if (nonSystemDrive is not null)
        {
            var resolvedRoot = BuildWindowsStorageRoot(nonSystemDrive.RootPath);
            return CreateAvailable(
                resolvedRoot,
                $"Using writable non-system drive '{NormalizeDriveRoot(nonSystemDrive.RootPath)}' for Feishu reply TTS storage.",
                useWindowsPaths: true);
        }

        var systemDrive = writableDrives.FirstOrDefault(d => IsSameDrive(d.RootPath, systemDriveRoot));
        if (systemDrive is not null && _options.AllowSystemDriveInstall)
        {
            var resolvedRoot = BuildWindowsStorageRoot(systemDrive.RootPath);
            return CreateAvailable(
                resolvedRoot,
                $"Using Windows system drive '{NormalizeDriveRoot(systemDrive.RootPath)}' for Feishu reply TTS storage because AllowSystemDriveInstall is enabled.",
                useWindowsPaths: true);
        }

        if (systemDrive is not null)
        {
            var driveLabel = NormalizeDriveRoot(systemDrive.RootPath);
            return new FeishuReplyTtsHealthStatus
            {
                IsAvailable = false,
                Message = $"Feishu reply TTS storage is unavailable because only the Windows system drive '{driveLabel}' is writable. Set FeishuReplyTts:TtsStorageRoot explicitly or enable AllowSystemDriveInstall."
            };
        }

        return new FeishuReplyTtsHealthStatus
        {
            IsAvailable = false,
            Message = "Feishu reply TTS storage is unavailable because no writable fixed drive was found on Windows. Set FeishuReplyTts:TtsStorageRoot explicitly or attach a writable data drive."
        };
    }

    private static FeishuReplyTtsHealthStatus CreateAvailable(string storageRoot, string message, bool useWindowsPaths)
    {
        return new FeishuReplyTtsHealthStatus
        {
            IsAvailable = true,
            StorageRoot = storageRoot,
            Message = message,
            ModelsRoot = AppendSegment(storageRoot, "models", useWindowsPaths),
            CacheRoot = AppendSegment(storageRoot, "cache", useWindowsPaths),
            TempRoot = AppendSegment(storageRoot, "temp", useWindowsPaths),
            LogsRoot = AppendSegment(storageRoot, "logs", useWindowsPaths),
            VenvRoot = AppendSegment(storageRoot, "venv", useWindowsPaths)
        };
    }

    private static string BuildWindowsStorageRoot(string driveRoot)
    {
        return AppendSegment(AppendSegment(NormalizeDriveRoot(driveRoot), "webcode", useWindowsPaths: true), "melotts", useWindowsPaths: true);
    }

    private static string AppendSegment(string root, string segment, bool useWindowsPaths)
    {
        var separator = useWindowsPaths ? '\\' : '/';
        var normalizedRoot = NormalizeStorageRoot(root, useWindowsPaths);
        var normalizedSegment = segment.Trim().Trim('\\', '/');

        if (normalizedRoot[^1] == separator)
        {
            return normalizedRoot + normalizedSegment;
        }

        return normalizedRoot + separator + normalizedSegment;
    }

    private static bool UsesWindowsSeparators(string path)
    {
        return path.Contains('\\', StringComparison.Ordinal) ||
               (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':');
    }

    private static bool IsSameDrive(string driveRoot, string systemDriveRoot)
    {
        return string.Equals(
            NormalizeDriveRoot(driveRoot),
            NormalizeDriveRoot(systemDriveRoot),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDriveRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }

        var normalized = root.Trim().Replace('/', '\\');
        if (normalized.Length == 2 && normalized[1] == ':')
        {
            return normalized + "\\";
        }

        normalized = normalized.TrimEnd('\\');
        if (normalized.Length == 2 && normalized[1] == ':')
        {
            return normalized + "\\";
        }

        if (!normalized.EndsWith('\\'))
        {
            normalized += "\\";
        }

        return normalized;
    }

    private static string NormalizeStorageRoot(string path, bool useWindowsPaths)
    {
        var separator = useWindowsPaths ? '\\' : '/';
        var alternateSeparator = useWindowsPaths ? '/' : '\\';
        var normalized = path.Trim().Replace(alternateSeparator, separator);

        while (normalized.Length > 1 && normalized.EndsWith(separator))
        {
            if (!useWindowsPaths && normalized == "/")
            {
                break;
            }

            if (useWindowsPaths && normalized.Length == 3 && normalized[1] == ':' && normalized[2] == '\\')
            {
                break;
            }

            normalized = normalized[..^1];
        }

        return normalized;
    }

    private sealed class SystemReplyTtsHostEnvironment : IReplyTtsHostEnvironment
    {
        public bool IsWindows => OperatingSystem.IsWindows();

        public string? SystemDriveRoot
        {
            get
            {
                if (!IsWindows)
                {
                    return null;
                }

                var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return string.IsNullOrWhiteSpace(systemDirectory)
                    ? Environment.GetEnvironmentVariable("SystemDrive")
                    : Path.GetPathRoot(systemDirectory);
            }
        }

        public IReadOnlyList<ReplyTtsDriveDescriptor> GetFixedDrives()
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed)
                .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .Select(drive => new ReplyTtsDriveDescriptor(
                    drive.RootDirectory.FullName,
                    drive.IsReady,
                    CanWriteToDrive(drive)))
                .ToArray();
        }

        private static bool CanWriteToDrive(DriveInfo drive)
        {
            if (!drive.IsReady)
            {
                return false;
            }

            try
            {
                var probeFilePath = Path.Combine(
                    drive.RootDirectory.FullName,
                    $".webcode-feishu-reply-tts-{Guid.NewGuid():N}.tmp");

                using var stream = new FileStream(
                    probeFilePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

public interface IReplyTtsHostEnvironment
{
    bool IsWindows { get; }

    string? SystemDriveRoot { get; }

    IReadOnlyList<ReplyTtsDriveDescriptor> GetFixedDrives();
}

public sealed class ReplyTtsDriveDescriptor
{
    public ReplyTtsDriveDescriptor(string rootPath, bool isReady, bool isWritable)
    {
        RootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        IsReady = isReady;
        IsWritable = isWritable;
    }

    public string RootPath { get; }

    public bool IsReady { get; }

    public bool IsWritable { get; }
}
