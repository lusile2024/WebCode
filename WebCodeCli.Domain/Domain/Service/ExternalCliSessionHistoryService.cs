using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

public interface IExternalCliSessionHistoryService
{
    /// <summary>
    /// 读取当前系统账户下外部 CLI 原生会话的最近历史消息
    /// </summary>
    Task<List<ExternalCliHistoryMessage>> GetRecentMessagesAsync(
        string toolId,
        string cliThreadId,
        int maxCount = 20,
        CancellationToken cancellationToken = default);
}

[ServiceDescription(typeof(IExternalCliSessionHistoryService), ServiceLifetime.Scoped)]
public class ExternalCliSessionHistoryService : IExternalCliSessionHistoryService
{
    private readonly ILogger<ExternalCliSessionHistoryService> _logger;

    public ExternalCliSessionHistoryService(ILogger<ExternalCliSessionHistoryService> logger)
    {
        _logger = logger;
    }

    public async Task<List<ExternalCliHistoryMessage>> GetRecentMessagesAsync(
        string toolId,
        string cliThreadId,
        int maxCount = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolId) || string.IsNullOrWhiteSpace(cliThreadId))
        {
            return [];
        }

        var normalizedToolId = NormalizeToolId(toolId);
        var normalizedThreadId = cliThreadId.Trim();
        var effectiveMaxCount = maxCount <= 0 ? 20 : maxCount;

        try
        {
            var messages = normalizedToolId switch
            {
                "codex" => await GetCodexMessagesAsync(normalizedThreadId, effectiveMaxCount, cancellationToken),
                "claude-code" => await GetClaudeCodeMessagesAsync(normalizedThreadId, effectiveMaxCount, cancellationToken),
                "opencode" => await GetOpenCodeMessagesAsync(normalizedThreadId, effectiveMaxCount, cancellationToken),
                _ => []
            };

            return messages
                .Where(message => !string.IsNullOrWhiteSpace(message.Role) && !string.IsNullOrWhiteSpace(message.Content))
                .OrderBy(message => message.CreatedAt ?? DateTime.MinValue)
                .TakeLast(effectiveMaxCount)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "读取外部 CLI 原生历史失败: ToolId={ToolId}, CliThreadId={CliThreadId}",
                normalizedToolId,
                normalizedThreadId);
            return [];
        }
    }

    protected virtual string? GetCodexSessionsRootPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? null
            : Path.Combine(userProfile, ".codex", "sessions");
    }

    protected virtual string? GetClaudeProjectsRootPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? null
            : Path.Combine(userProfile, ".claude", "projects");
    }

    protected virtual async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var waitForExitTask = process.WaitForExitAsync(cancellationToken);

        var completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(timeout, cancellationToken));
        if (completedTask != waitForExitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            return (-1, string.Empty, $"Process timeout after {timeout.TotalSeconds:F0}s");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    private async Task<List<ExternalCliHistoryMessage>> GetCodexMessagesAsync(
        string cliThreadId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var filePath = FindCodexRolloutFile(cliThreadId, cancellationToken);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return [];
        }

        return await ParseCodexRolloutFileAsync(filePath, maxCount, cancellationToken);
    }

    private async Task<List<ExternalCliHistoryMessage>> GetClaudeCodeMessagesAsync(
        string cliThreadId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var transcriptPath = FindClaudeTranscriptFile(cliThreadId, cancellationToken);
        if (string.IsNullOrWhiteSpace(transcriptPath) || !File.Exists(transcriptPath))
        {
            return [];
        }

        return await ParseClaudeTranscriptFileAsync(transcriptPath, maxCount, cancellationToken);
    }

    private async Task<List<ExternalCliHistoryMessage>> GetOpenCodeMessagesAsync(
        string cliThreadId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var escapedSessionId = cliThreadId.Replace("\"", "\\\"", StringComparison.Ordinal);
        var (exitCode, stdout, stderr) = await RunProcessAsync(
            "opencode",
            $"export {escapedSessionId}",
            TimeSpan.FromSeconds(15),
            cancellationToken);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogDebug(
                "OpenCode export 失败: ExitCode={ExitCode}, CliThreadId={CliThreadId}, Stderr={Stderr}",
                exitCode,
                cliThreadId,
                Truncate(stderr, 400));
            return [];
        }

        return ParseOpenCodeExport(stdout, maxCount);
    }

    private string? FindCodexRolloutFile(string cliThreadId, CancellationToken cancellationToken)
    {
        var sessionsRoot = GetCodexSessionsRootPath();
        if (string.IsNullOrWhiteSpace(sessionsRoot) || !Directory.Exists(sessionsRoot))
        {
            return null;
        }

        try
        {
            var directCandidates = Directory
                .EnumerateFiles(sessionsRoot, $"*{cliThreadId}*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            if (directCandidates.Count > 0)
            {
                return directCandidates[0];
            }

            foreach (var file in Directory.EnumerateFiles(sessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var firstLine = ReadFirstNonEmptyLine(file, maxLines: 3);
                if (string.IsNullOrWhiteSpace(firstLine))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(firstLine);
                    var root = document.RootElement;
                    if (!TryGetProperty(root, "payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var sessionId = GetString(payload, "id");
                    if (string.Equals(sessionId, cliThreadId, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
                catch
                {
                    // ignore broken lines
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "定位 Codex rollout 文件失败");
        }

        return null;
    }

    private string? FindClaudeTranscriptFile(string cliThreadId, CancellationToken cancellationToken)
    {
        var projectsRoot = GetClaudeProjectsRootPath();
        if (string.IsNullOrWhiteSpace(projectsRoot) || !Directory.Exists(projectsRoot))
        {
            return null;
        }

        try
        {
            foreach (var indexFile in Directory.EnumerateFiles(projectsRoot, "sessions-index.json", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var json = File.ReadAllText(indexFile);
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(json);
                if (!TryGetProperty(document.RootElement, "entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var sessionId = GetString(entry, "sessionId", "session_id");
                    if (!string.Equals(sessionId, cliThreadId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var fullPath = GetString(entry, "fullPath", "full_path");
                    if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }

            var transcriptCandidates = Directory
                .EnumerateFiles(projectsRoot, $"{cliThreadId}.jsonl", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}subagents{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            return transcriptCandidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "定位 Claude Code transcript 文件失败");
            return null;
        }
    }

    private async Task<List<ExternalCliHistoryMessage>> ParseCodexRolloutFileAsync(
        string filePath,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var messages = new List<ExternalCliHistoryMessage>();

        await foreach (var line in ReadLinesAsync(filePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!string.Equals(GetString(root, "type"), "response_item", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetProperty(root, "payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!string.Equals(GetString(payload, "type"), "message", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var role = GetString(payload, "role");
                if (!IsSupportedRole(role))
                {
                    continue;
                }

                var content = ExtractCodexMessageContent(payload);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                messages.Add(new ExternalCliHistoryMessage
                {
                    Role = role!,
                    Content = content,
                    CreatedAt = GetDateTime(root, "timestamp") ?? GetDateTime(payload, "timestamp"),
                    RawType = "codex.message"
                });
            }
            catch (JsonException)
            {
                // ignore bad lines
            }
        }

        return messages.TakeLast(maxCount).ToList();
    }

    private async Task<List<ExternalCliHistoryMessage>> ParseClaudeTranscriptFileAsync(
        string filePath,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var messages = new List<ExternalCliHistoryMessage>();

        await foreach (var line in ReadLinesAsync(filePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var recordType = GetString(root, "type");
                if (!IsSupportedRole(recordType))
                {
                    continue;
                }

                if (!TryGetProperty(root, "message", out var messageElement) || messageElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var role = GetString(messageElement, "role") ?? recordType;
                if (!IsSupportedRole(role))
                {
                    continue;
                }

                var content = ExtractClaudeMessageContent(messageElement);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                messages.Add(new ExternalCliHistoryMessage
                {
                    Role = role!,
                    Content = content,
                    CreatedAt = GetDateTime(root, "timestamp"),
                    RawType = $"claude.{recordType}"
                });
            }
            catch (JsonException)
            {
                // ignore bad lines
            }
        }

        return messages.TakeLast(maxCount).ToList();
    }

    private List<ExternalCliHistoryMessage> ParseOpenCodeExport(string json, int maxCount)
    {
        var messages = new List<ExternalCliHistoryMessage>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryGetProperty(document.RootElement, "messages", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return messages;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryGetProperty(item, "info", out var info) || info.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var role = GetString(info, "role");
                if (!IsSupportedRole(role))
                {
                    continue;
                }

                if (!TryGetProperty(item, "parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var content = ExtractTextParts(parts, "text");
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                DateTime? createdAt = null;
                if (TryGetProperty(info, "time", out var timeElement) && timeElement.ValueKind == JsonValueKind.Object)
                {
                    createdAt = GetDateTime(timeElement, "created");
                }

                messages.Add(new ExternalCliHistoryMessage
                {
                    Role = role!,
                    Content = content,
                    CreatedAt = createdAt,
                    RawType = "opencode.message"
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "解析 OpenCode export JSON 失败");
        }

        return messages.TakeLast(maxCount).ToList();
    }

    private static string? NormalizeToolId(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return null;
        }

        if (toolId.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            return "claude-code";
        }

        if (toolId.Equals("opencode-cli", StringComparison.OrdinalIgnoreCase))
        {
            return "opencode";
        }

        return toolId.Trim();
    }

    private static bool IsSupportedRole(string? role)
    {
        return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
               || string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCodexMessageContent(JsonElement payload)
    {
        if (!TryGetProperty(payload, "content", out var contentElement))
        {
            return string.Empty;
        }

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString() ?? string.Empty;
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return ExtractTextParts(contentElement, "input_text", "output_text", "text");
    }

    private static string ExtractClaudeMessageContent(JsonElement messageElement)
    {
        if (!TryGetProperty(messageElement, "content", out var contentElement))
        {
            return string.Empty;
        }

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString() ?? string.Empty;
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return ExtractTextParts(contentElement, "text");
    }

    private static string ExtractTextParts(JsonElement items, params string[] supportedPartTypes)
    {
        if (items.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var texts = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var textValue = item.GetString();
                if (!string.IsNullOrWhiteSpace(textValue))
                {
                    texts.Add(textValue.Trim());
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = GetString(item, "type");
            if (string.IsNullOrWhiteSpace(type)
                || !supportedPartTypes.Any(candidate => string.Equals(candidate, type, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var text = GetString(item, "text", "content");
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text.Trim());
            }
        }

        return string.Join("\n", texts.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string? ReadFirstNonEmptyLine(string filePath, int maxLines)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream);
        for (var index = 0; index < maxLines && !reader.EndOfStream; index++)
        {
            var line = reader.ReadLine();
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line != null)
            {
                yield return line;
            }
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static DateTime? GetDateTime(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var parsedDateTime))
            {
                return parsedDateTime;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unixValue))
            {
                if (unixValue > 10_000_000_000)
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(unixValue).LocalDateTime;
                }

                return DateTimeOffset.FromUnixTimeSeconds(unixValue).LocalDateTime;
            }
        }

        return null;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..maxLength] + "...";
    }
}
