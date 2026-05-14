using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

[ServiceDescription(typeof(IAttachmentStagingService), ServiceLifetime.Scoped)]
public class AttachmentStagingService : IAttachmentStagingService
{
    private const string HiddenRootDirectoryName = ".webcode";
    private const string MessageInputDirectoryName = "message-inputs";

    private readonly ICliExecutorService _cliExecutorService;
    private readonly ILogger<AttachmentStagingService> _logger;

    public AttachmentStagingService(
        ICliExecutorService cliExecutorService,
        ILogger<AttachmentStagingService> logger)
    {
        _cliExecutorService = cliExecutorService;
        _logger = logger;
    }

    public async Task<List<StagedMessageAttachment>> StageAsync(
        string sessionId,
        string submissionId,
        IReadOnlyCollection<MessageDraftAttachmentInput> attachments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionId);
        ArgumentNullException.ThrowIfNull(attachments);

        if (attachments.Count == 0)
        {
            return [];
        }

        var workspaceRoot = _cliExecutorService.GetSessionWorkspacePath(sessionId);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new InvalidOperationException($"Could not resolve a workspace path for session '{sessionId}'.");
        }

        var workspaceFullPath = Path.GetFullPath(workspaceRoot);
        var hiddenRoot = Path.Combine(workspaceFullPath, HiddenRootDirectoryName);
        var messageInputsRoot = Path.Combine(hiddenRoot, MessageInputDirectoryName);
        var submissionDirectoryName = NormalizePathSegment(submissionId, "submission");
        var submissionRoot = Path.Combine(messageInputsRoot, submissionDirectoryName);

        Directory.CreateDirectory(submissionRoot);
        TryHideDirectory(hiddenRoot);

        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stagedAttachments = new List<StagedMessageAttachment>(attachments.Count);

        foreach (var attachment in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var displayName = string.IsNullOrWhiteSpace(attachment.FileName)
                ? "attachment"
                : attachment.FileName.Trim();
            var stagedFileName = CreateUniqueFileName(displayName, usedFileNames);
            var stagedAbsolutePath = Path.Combine(submissionRoot, stagedFileName);
            var stagedFullPath = Path.GetFullPath(stagedAbsolutePath);

            EnsurePathWithinRoot(submissionRoot, stagedFullPath, displayName);

            await File.WriteAllBytesAsync(stagedFullPath, attachment.Content, cancellationToken);

            var extension = Path.GetExtension(displayName);
            var metadata = new MessageAttachment
            {
                Id = attachment.Id,
                DisplayName = displayName,
                MimeType = string.IsNullOrWhiteSpace(attachment.ContentType)
                    ? "application/octet-stream"
                    : attachment.ContentType.Trim(),
                Extension = extension,
                SizeBytes = attachment.Content.LongLength,
                Kind = MessageSubmissionService.DetectAttachmentKind(displayName, attachment.ContentType),
                WorkspaceRelativePath = BuildWorkspaceRelativePath(submissionDirectoryName, stagedFileName),
                CreatedAt = DateTime.UtcNow
            };

            stagedAttachments.Add(new StagedMessageAttachment
            {
                InputId = attachment.Id,
                DisplayName = displayName,
                AbsolutePath = stagedFullPath,
                Metadata = metadata
            });
        }

        _logger.LogDebug(
            "Staged {Count} attachment(s) for session {SessionId} under {SubmissionRoot}",
            stagedAttachments.Count,
            sessionId,
            submissionRoot);

        return stagedAttachments;
    }

    private static string CreateUniqueFileName(string displayName, HashSet<string> usedFileNames)
    {
        var normalizedBaseName = NormalizeFileName(displayName);
        var baseName = Path.GetFileNameWithoutExtension(normalizedBaseName);
        var extension = Path.GetExtension(normalizedBaseName);
        var candidate = normalizedBaseName;
        var suffix = 2;

        while (!usedFileNames.Add(candidate))
        {
            candidate = $"{baseName}-{suffix}{extension}";
            suffix++;
        }

        return candidate;
    }

    private static string NormalizeFileName(string fileName)
    {
        var fileNameOnly = Path.GetFileName(fileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileNameOnly))
        {
            return "attachment";
        }

        var sanitizedChars = fileNameOnly
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)
            .ToArray();
        var sanitized = new string(sanitizedChars).Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "attachment";
        }

        sanitized = sanitized.Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "attachment";
        }

        return sanitized;
    }

    private static string NormalizePathSegment(string segment, string fallback)
    {
        var sanitizedChars = segment
            .Trim()
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch)
            .ToArray();
        var sanitized = new string(sanitizedChars).Trim('.', ' ');

        return string.IsNullOrWhiteSpace(sanitized)
            ? fallback
            : sanitized;
    }

    private static string BuildWorkspaceRelativePath(string submissionDirectoryName, string stagedFileName)
    {
        return $"{HiddenRootDirectoryName}/{MessageInputDirectoryName}/{submissionDirectoryName}/{stagedFileName}";
    }

    private static void EnsurePathWithinRoot(string rootPath, string candidatePath, string displayName)
    {
        var normalizedRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!candidatePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Attachment '{displayName}' resolved outside the staging root.");
        }
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path[^1] == Path.DirectorySeparatorChar || path[^1] == Path.AltDirectorySeparatorChar
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void TryHideDirectory(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(path))
            {
                return;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Hidden) == 0)
            {
                File.SetAttributes(path, attributes | FileAttributes.Hidden);
            }
        }
        catch
        {
        }
    }
}
