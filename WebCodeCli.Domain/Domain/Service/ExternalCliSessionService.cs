using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Domain.Service;

public interface IExternalCliSessionService
{
    /// <summary>
    /// 发现当前操作系统账户下的外部 CLI 会话（并标记当前 Web 用户是否已导入）
    /// </summary>
    Task<List<ExternalCliSessionSummary>> DiscoverAsync(
        string username,
        string? toolId = null,
        int maxCount = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 导入外部 CLI 会话为 WebCode 会话
    /// </summary>
    Task<ImportExternalCliSessionResult> ImportAsync(
        string username,
        ImportExternalCliSessionRequest request,
        string? feishuChatKey = null,
        CancellationToken cancellationToken = default);
}

[ServiceDescription(typeof(IExternalCliSessionService), ServiceLifetime.Scoped)]
public sealed class ExternalCliSessionService : IExternalCliSessionService
{
    private readonly ILogger<ExternalCliSessionService> _logger;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IUserWorkspacePolicyService _userWorkspacePolicyService;
    private readonly string[] _globalAllowedRoots;

    public ExternalCliSessionService(
        ILogger<ExternalCliSessionService> logger,
        IChatSessionRepository chatSessionRepository,
        IUserWorkspacePolicyService userWorkspacePolicyService,
        IConfiguration configuration)
    {
        _logger = logger;
        _chatSessionRepository = chatSessionRepository;
        _userWorkspacePolicyService = userWorkspacePolicyService;
        _globalAllowedRoots = (configuration.GetSection("Workspace:AllowedRoots").Get<string[]>() ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeWorkspacePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<List<ExternalCliSessionSummary>> DiscoverAsync(
        string username,
        string? toolId = null,
        int maxCount = 20,
        CancellationToken cancellationToken = default)
    {
        if (maxCount <= 0)
        {
            maxCount = 20;
        }

        var normalizedToolId = NormalizeToolId(toolId);

        var toolsToScan = new List<string>();
        if (string.IsNullOrWhiteSpace(normalizedToolId))
        {
            toolsToScan.Add("opencode");
            toolsToScan.Add("codex");
            toolsToScan.Add("claude-code");
        }
        else
        {
            toolsToScan.Add(normalizedToolId);
        }

        var discovered = new List<ExternalCliSessionSummary>();
        foreach (var t in toolsToScan.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(t, "opencode", StringComparison.OrdinalIgnoreCase))
            {
                discovered.AddRange(await DiscoverOpenCodeSessionsAsync(maxCount, cancellationToken));
                continue;
            }

            if (string.Equals(t, "codex", StringComparison.OrdinalIgnoreCase))
            {
                discovered.AddRange(await DiscoverCodexSessionsFromDiskAsync(maxCount, cancellationToken));
                continue;
            }

            if (string.Equals(t, "claude-code", StringComparison.OrdinalIgnoreCase))
            {
                discovered.AddRange(await DiscoverClaudeCodeSessionsFromDiskAsync(maxCount, cancellationToken));
                continue;
            }
        }

        // 标记已导入（仅对当前 Web 用户）
        Dictionary<string, string> importedMap = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var imported = await _chatSessionRepository.GetByUsernameOrderByUpdatedAtAsync(username);
            importedMap = imported
                .Where(s => !string.IsNullOrWhiteSpace(s.ToolId) && !string.IsNullOrWhiteSpace(s.CliThreadId))
                .GroupBy(s => BuildImportKey(NormalizeToolId(s.ToolId), s.CliThreadId!), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().SessionId, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "标记外部会话已导入状态失败，忽略");
        }

        // 发现阶段直接过滤：
        // 1) 工作区不存在/为空：不显示
        // 2) 不在白名单：不显示
        // 3) (ToolId, CliThreadId) 已被其他用户占用：不显示
        var (effectiveRoots, _, rootsLoadError) = await GetEffectiveAllowedRootsForUserAsync(username, cancellationToken);
        if (!string.IsNullOrWhiteSpace(rootsLoadError))
        {
            _logger.LogDebug("加载用户白名单/允许根目录失败: {Error}", rootsLoadError);
            return new List<ExternalCliSessionSummary>();
        }

        var filtered = new List<ExternalCliSessionSummary>();
        foreach (var item in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            item.ToolId = NormalizeToolId(item.ToolId);
            item.CliThreadId = item.CliThreadId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(item.ToolId) || string.IsNullOrWhiteSpace(item.CliThreadId))
            {
                continue;
            }

            var workspacePath = NormalizeWorkspacePath(item.WorkspacePath);
            if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
            {
                continue;
            }

            if (effectiveRoots.Length > 0 && !effectiveRoots.Any(root => IsPathWithinRoot(root, workspacePath)))
            {
                continue;
            }

            item.WorkspacePath = workspacePath;

            var importKey = BuildImportKey(item.ToolId, item.CliThreadId);
            if (importedMap.TryGetValue(importKey, out var importedSessionId))
            {
                item.AlreadyImported = true;
                item.ImportedSessionId = importedSessionId;
                filtered.Add(item);
                continue;
            }

            try
            {
                var occupied = await _chatSessionRepository.GetByToolAndCliThreadIdAsync(item.ToolId, item.CliThreadId);
                if (occupied != null)
                {
                    if (string.Equals(occupied.Username, username, StringComparison.OrdinalIgnoreCase))
                    {
                        item.AlreadyImported = true;
                        item.ImportedSessionId = occupied.SessionId;
                        filtered.Add(item);
                    }

                    // 已被其他用户占用时：发现阶段直接不显示，避免泄露
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "检查外部会话占用失败，忽略该条外部会话记录");
                continue;
            }

            filtered.Add(item);
        }

        return filtered
            .OrderByDescending(x => x.UpdatedAt ?? DateTime.MinValue)
            .Take(maxCount)
            .ToList();
    }

    public async Task<ImportExternalCliSessionResult> ImportAsync(
        string username,
        ImportExternalCliSessionRequest request,
        string? feishuChatKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new ImportExternalCliSessionResult
            {
                Success = false,
                ErrorMessage = "用户名不能为空。"
            };
        }

        var toolId = NormalizeToolId(request.ToolId);
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return new ImportExternalCliSessionResult
            {
                Success = false,
                ErrorMessage = "ToolId 不能为空。"
            };
        }

        if (string.IsNullOrWhiteSpace(request.CliThreadId))
        {
            return new ImportExternalCliSessionResult
            {
                Success = false,
                ErrorMessage = "CliThreadId 不能为空。"
            };
        }

        var cliThreadId = request.CliThreadId.Trim();

        // 幂等：同一个用户 + tool + threadId 只导入一次
        var existing = await _chatSessionRepository.GetByUsernameToolAndCliThreadIdAsync(
            username,
            toolId,
            cliThreadId);

        if (existing != null)
        {
            return new ImportExternalCliSessionResult
            {
                Success = true,
                AlreadyExists = true,
                SessionId = existing.SessionId
            };
        }

        // 跨用户占用检查：同一个 (ToolId, CliThreadId) 只能属于一个 WebCode 用户
        var occupied = await _chatSessionRepository.GetByToolAndCliThreadIdAsync(toolId, cliThreadId);
        if (occupied != null)
        {
            if (string.Equals(occupied.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                // 同一用户重复导入：按幂等处理
                return new ImportExternalCliSessionResult
                {
                    Success = true,
                    AlreadyExists = true,
                    SessionId = occupied.SessionId
                };
            }

            return new ImportExternalCliSessionResult
            {
                Success = false,
                ErrorMessage = "导入失败：该外部会话已被其他用户占用。"
            };
        }

        var now = DateTime.Now;
        var normalizedChatKey = string.IsNullOrWhiteSpace(feishuChatKey) ? null : feishuChatKey.Trim().ToLowerInvariant();
        var title = BuildImportedTitle(toolId, request.Title, cliThreadId);
        var workspacePath = NormalizeWorkspacePath(request.WorkspacePath);
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return new ImportExternalCliSessionResult
            {
                Success = false,
                ErrorMessage = "导入失败：WorkspacePath 不能为空（用于白名单校验）。"
            };
        }

        // 白名单/允许范围校验（导入阶段必须再校验一次，防止绕过发现接口）
        var (workspaceAllowed, deniedReason) = await ValidateWorkspacePathAsync(username, workspacePath, cancellationToken);
        if (!workspaceAllowed)
        {
            return new ImportExternalCliSessionResult
            {
                Success = false,
                ErrorMessage = deniedReason ?? "导入失败：工作区路径不在允许范围内。"
            };
        }

        if (!Directory.Exists(workspacePath))
        {
            return new ImportExternalCliSessionResult
            {
                Success = false,
                ErrorMessage = "导入失败：工作区目录不存在或已被删除。"
            };
        }

        var isWorkspaceValid = !string.IsNullOrWhiteSpace(workspacePath) && Directory.Exists(workspacePath);
        var isCustomWorkspace = !string.IsNullOrWhiteSpace(workspacePath);

        string sessionId;
        if (!string.IsNullOrWhiteSpace(normalizedChatKey))
        {
            // 飞书导入：创建会话并设为活跃（复用仓储事务逻辑）
            sessionId = await _chatSessionRepository.CreateFeishuSessionAsync(normalizedChatKey, username, workspacePath, toolId);

            var entity = await _chatSessionRepository.GetByIdAsync(sessionId);
            if (entity == null)
            {
                return new ImportExternalCliSessionResult
                {
                    Success = false,
                    ErrorMessage = "导入失败：创建会话后无法读取会话记录。"
                };
            }

            entity.Title = title;
            entity.ToolId = toolId;
            entity.CliThreadId = cliThreadId;
            entity.WorkspacePath = workspacePath;
            entity.IsCustomWorkspace = isCustomWorkspace;
            entity.IsWorkspaceValid = isWorkspaceValid;
            entity.UpdatedAt = now;

            await _chatSessionRepository.InsertOrUpdateAsync(entity);
        }
        else
        {
            sessionId = Guid.NewGuid().ToString();
            var entity = new ChatSessionEntity
            {
                SessionId = sessionId,
                Username = username,
                Title = title,
                ToolId = toolId,
                CliThreadId = cliThreadId,
                WorkspacePath = workspacePath,
                IsCustomWorkspace = isCustomWorkspace,
                IsWorkspaceValid = isWorkspaceValid,
                CreatedAt = now,
                UpdatedAt = now
            };

            var success = await _chatSessionRepository.InsertOrUpdateAsync(entity);
            if (!success)
            {
                return new ImportExternalCliSessionResult
                {
                    Success = false,
                    ErrorMessage = "导入失败：写入会话记录失败。"
                };
            }
        }

        return new ImportExternalCliSessionResult
        {
            Success = true,
            AlreadyExists = false,
            SessionId = sessionId
        };
    }

    private async Task<List<ExternalCliSessionSummary>> DiscoverOpenCodeSessionsAsync(int maxCount, CancellationToken cancellationToken)
    {
        var list = new List<ExternalCliSessionSummary>();

        // 尽量使用 CLI 自身的 session list 输出，避免 OS/版本差异
        var (exitCode, stdout, stderr) = await RunProcessAsync(
            fileName: "opencode",
            arguments: $"session list --format json -n {maxCount}",
            timeout: TimeSpan.FromSeconds(8),
            cancellationToken: cancellationToken);

        if (exitCode != 0)
        {
            // opencode 未安装时通常是：exitCode=127 或 stderr 提示 not found
            _logger.LogDebug("OpenCode session list 失败: ExitCode={ExitCode}, Stderr={Stderr}", exitCode, Truncate(stderr, 500));
            return list;
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return list;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            JsonElement sessions;
            if (root.ValueKind == JsonValueKind.Array)
            {
                sessions = root;
            }
            else if (TryGetProperty(root, "sessions", out var sessionsEl) && sessionsEl.ValueKind == JsonValueKind.Array)
            {
                sessions = sessionsEl;
            }
            else if (TryGetProperty(root, "data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
            {
                sessions = dataEl;
            }
            else
            {
                return list;
            }

            foreach (var item in sessions.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = GetString(item, "id", "sessionID", "sessionId", "session_id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var title = GetString(item, "title", "name");

                // cwd / project path
                string? workspacePath = GetString(item, "cwd", "path", "directory", "projectPath", "project_path");
                if (string.IsNullOrWhiteSpace(workspacePath) && TryGetProperty(item, "project", out var projectEl))
                {
                    if (projectEl.ValueKind == JsonValueKind.String)
                    {
                        workspacePath = projectEl.GetString();
                    }
                    else if (projectEl.ValueKind == JsonValueKind.Object)
                    {
                        workspacePath = GetString(projectEl, "path", "cwd", "directory");
                    }
                }

                var updatedAt = GetDateTime(item, "updatedAt", "updated_at", "updated", "lastUpdatedAt", "last_updated_at", "timestamp");

                list.Add(new ExternalCliSessionSummary
                {
                    ToolId = "opencode",
                    ToolName = "OpenCode",
                    CliThreadId = id,
                    Title = title,
                    WorkspacePath = string.IsNullOrWhiteSpace(workspacePath) ? null : workspacePath,
                    UpdatedAt = updatedAt
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "解析 OpenCode session list JSON 失败，忽略");
        }

        return list;
    }

    private Task<List<ExternalCliSessionSummary>> DiscoverCodexSessionsFromDiskAsync(int maxCount, CancellationToken cancellationToken)
    {
        var list = new List<ExternalCliSessionSummary>();

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                return Task.FromResult(list);
            }

            var sessionsRoot = Path.Combine(userProfile, ".codex", "sessions");
            if (!Directory.Exists(sessionsRoot))
            {
                return Task.FromResult(list);
            }

            var files = Directory
                .EnumerateFiles(sessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(Math.Max(maxCount * 10, 50))
                .ToList();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fi in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = ReadFirstNonEmptyLine(fi.FullName, maxLines: 3);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var payload = root;
                    if (TryGetProperty(root, "payload", out var payloadEl) && payloadEl.ValueKind == JsonValueKind.Object)
                    {
                        payload = payloadEl;
                    }

                    var id = GetString(payload, "id") ?? GetString(root, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    if (!seen.Add(id))
                    {
                        continue;
                    }

                    var cwd = GetString(payload, "cwd", "workspacePath", "workspace_path", "workspace");
                    var title = GetString(payload, "title", "name", "firstPrompt", "first_prompt");
                    var updatedAt = GetDateTime(payload, "timestamp") ??
                                    GetDateTime(root, "timestamp") ??
                                    fi.LastWriteTime;

                    list.Add(new ExternalCliSessionSummary
                    {
                        ToolId = "codex",
                        ToolName = "Codex",
                        CliThreadId = id,
                        Title = SanitizeTitle(title),
                        WorkspacePath = string.IsNullOrWhiteSpace(cwd) ? null : cwd,
                        UpdatedAt = updatedAt
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "解析 Codex rollout JSON 失败，忽略: {File}", fi.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "发现 Codex 外部会话失败，忽略");
        }

        return Task.FromResult(list);
    }

    private Task<List<ExternalCliSessionSummary>> DiscoverClaudeCodeSessionsFromDiskAsync(int maxCount, CancellationToken cancellationToken)
    {
        var list = new List<ExternalCliSessionSummary>();

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                return Task.FromResult(list);
            }

            var projectsRoot = Path.Combine(userProfile, ".claude", "projects");
            if (!Directory.Exists(projectsRoot))
            {
                return Task.FromResult(list);
            }

            // 优先使用 sessions-index.json（更快，不需要解析巨大的 session jsonl 文件）
            list.AddRange(DiscoverClaudeCodeSessionsFromIndex(projectsRoot, cancellationToken));

            // 兜底：如果索引缺失/为空，则扫描最近的会话 jsonl
            if (list.Count == 0)
            {
                list.AddRange(DiscoverClaudeCodeSessionsFromJsonlFiles(projectsRoot, maxCount, cancellationToken));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "发现 Claude Code 外部会话失败，忽略");
        }

        // 去重
        var dedup = list
            .Where(x => !string.IsNullOrWhiteSpace(x.CliThreadId))
            .GroupBy(x => x.CliThreadId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.UpdatedAt ?? DateTime.MinValue).First())
            .ToList();

        return Task.FromResult(dedup);
    }

    private List<ExternalCliSessionSummary> DiscoverClaudeCodeSessionsFromIndex(string projectsRoot, CancellationToken cancellationToken)
    {
        var list = new List<ExternalCliSessionSummary>();

        IEnumerable<string> indexFiles;
        try
        {
            indexFiles = Directory.EnumerateFiles(projectsRoot, "sessions-index.json", SearchOption.AllDirectories);
        }
        catch
        {
            return list;
        }

        foreach (var indexFile in indexFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = File.ReadAllText(indexFile);
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!TryGetProperty(root, "entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in entries.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var sessionId = GetString(entry, "sessionId", "session_id");
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        continue;
                    }

                    var cwd = GetString(entry, "projectPath", "project_path", "cwd");
                    var title = GetString(entry, "firstPrompt", "first_prompt", "title", "name");
                    var updatedAt = GetDateTime(entry, "modified", "updatedAt", "updated_at", "fileMtime", "file_mtime", "created");

                    list.Add(new ExternalCliSessionSummary
                    {
                        ToolId = "claude-code",
                        ToolName = "Claude Code",
                        CliThreadId = sessionId,
                        Title = SanitizeTitle(title),
                        WorkspacePath = string.IsNullOrWhiteSpace(cwd) ? null : cwd,
                        UpdatedAt = updatedAt
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "解析 Claude sessions-index.json 失败，忽略: {File}", indexFile);
            }
        }

        return list;
    }

    private List<ExternalCliSessionSummary> DiscoverClaudeCodeSessionsFromJsonlFiles(string projectsRoot, int maxCount, CancellationToken cancellationToken)
    {
        var list = new List<ExternalCliSessionSummary>();

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(projectsRoot, "*.jsonl", SearchOption.AllDirectories)
                .Where(IsClaudeSessionTranscriptFile)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(Math.Max(maxCount * 20, 200))
                .ToList();
        }
        catch
        {
            return list;
        }

        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = ReadFirstNonEmptyLine(file, maxLines: 10);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var sessionId = GetString(root, "sessionId", "session_id") ?? Path.GetFileNameWithoutExtension(file);
                var cwd = GetString(root, "cwd", "projectPath", "project_path");
                var updatedAt = GetDateTime(root, "timestamp") ?? File.GetLastWriteTime(file);

                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    continue;
                }

                list.Add(new ExternalCliSessionSummary
                {
                    ToolId = "claude-code",
                    ToolName = "Claude Code",
                    CliThreadId = sessionId,
                    WorkspacePath = string.IsNullOrWhiteSpace(cwd) ? null : cwd,
                    UpdatedAt = updatedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "解析 Claude session jsonl 失败，忽略: {File}", file);
            }
        }

        return list;
    }

    private static bool IsClaudeSessionTranscriptFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var lower = filePath.Replace('/', '\\').ToLowerInvariant();
        if (lower.Contains("\\subagents\\", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("\\tool-results\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        return Guid.TryParse(name, out _);
    }

    private static string? ReadFirstNonEmptyLine(string filePath, int maxLines)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            for (var i = 0; i < Math.Max(maxLines, 1); i++)
            {
                var line = reader.ReadLine();
                if (line == null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? NormalizeWorkspacePath(string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(workspacePath.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return workspacePath.Trim();
        }
    }

    private async Task<bool> IsWorkspacePathAllowedAsync(string username, string workspacePath, CancellationToken cancellationToken)
    {
        var (allowed, _) = await ValidateWorkspacePathAsync(username, workspacePath, cancellationToken);
        return allowed;
    }

    private async Task<(bool Allowed, string? DeniedReason)> ValidateWorkspacePathAsync(
        string username,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(username))
        {
            return (false, "导入失败：用户名为空。");
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, "导入失败：WorkspacePath 不能为空。");
        }

        var normalizedTarget = NormalizeWorkspacePath(workspacePath);
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return (false, "导入失败：WorkspacePath 无效。");
        }

        var (effectiveRoots, userRoots, rootsLoadError) =
            await GetEffectiveAllowedRootsForUserAsync(username, cancellationToken);
        if (!string.IsNullOrWhiteSpace(rootsLoadError))
        {
            return (false, "导入失败：读取用户白名单目录失败。");
        }

        // 若配置了 roots，则必须位于 roots 内；若未配置，则放行（保持与现有逻辑一致）
        if (effectiveRoots.Length > 0 && !effectiveRoots.Any(root => IsPathWithinRoot(root, normalizedTarget)))
        {
            if (userRoots.Length > 0)
            {
                return (false, "导入失败：工作区路径不在该用户白名单目录内。");
            }

            return (false, "导入失败：工作区路径不在系统允许范围 (Workspace:AllowedRoots) 内。");
        }

        return (true, null);
    }

    private async Task<(string[] EffectiveRoots, string[] UserRoots, string? Error)> GetEffectiveAllowedRootsForUserAsync(
        string username,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(username))
        {
            return (Array.Empty<string>(), Array.Empty<string>(), "Username is empty.");
        }

        try
        {
            var userRoots = (await _userWorkspacePolicyService.GetAllowedDirectoriesAsync(username))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeWorkspacePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var effectiveRoots = userRoots.Length > 0 ? userRoots : _globalAllowedRoots;
            return (effectiveRoots, userRoots, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取用户白名单目录失败，按不允许处理");
            return (Array.Empty<string>(), Array.Empty<string>(), ex.Message);
        }
    }

    private static bool IsPathWithinRoot(string rootPath, string targetPath)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalizedTarget.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedTarget.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildImportKey(string toolId, string cliThreadId)
    {
        return $"{NormalizeToolId(toolId)}::{cliThreadId.Trim()}";
    }

    private static string? SanitizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // 去掉控制字符（例如 sessions-index.json 里可能包含 \b）
        var cleaned = new string(title.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string NormalizeToolId(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return string.Empty;
        }

        if (toolId.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            return "claude-code";
        }

        if (toolId.Equals("opencode-cli", StringComparison.OrdinalIgnoreCase))
        {
            return "opencode";
        }

        return toolId.Trim().ToLowerInvariant();
    }

    private static string BuildImportedTitle(string toolId, string? title, string cliThreadId)
    {
        var toolName = toolId switch
        {
            "codex" => "Codex",
            "claude-code" => "Claude Code",
            "opencode" => "OpenCode",
            _ => toolId
        };

        var baseTitle = string.IsNullOrWhiteSpace(title) ? cliThreadId : title.Trim();
        if (baseTitle.Length > 80)
        {
            baseTitle = baseTitle.Substring(0, 80) + "...";
        }

        return $"[{toolName}] {baseTitle}";
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return (-1, string.Empty, "Process start failed.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            await process.WaitForExitAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            return (-2, string.Empty, "Timeout.");
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        // JsonElement 属性名大小写敏感，这里统一尝试一组常见变体
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }

            var alt = char.IsUpper(name[0])
                ? char.ToLowerInvariant(name[0]) + name.Substring(1)
                : char.ToUpperInvariant(name[0]) + name.Substring(1);
            if (element.TryGetProperty(alt, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
                if (value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetRawText();
                }
            }
        }

        return null;
    }

    private static DateTime? GetDateTime(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString();
                if (DateTime.TryParse(s, out var dt))
                {
                    return dt;
                }
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt64(out var n))
                {
                    // heuristic: 13 位以上当作毫秒
                    if (n >= 1_000_000_000_000)
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(n).LocalDateTime;
                    }
                    if (n >= 1_000_000_000)
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(n).LocalDateTime;
                    }
                }
            }
        }

        return null;
    }

    private static string Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        if (text.Length <= maxLen)
        {
            return text;
        }
        return text.Substring(0, maxLen) + "...";
    }
}
