using System.Text.RegularExpressions;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Domain.Domain.Service.Channels;

internal static partial class FeishuStreamingLaunchStateResolver
{
    public static async Task<(string? Model, string? ReasoningEffort)> ResolveAsync(
        ChatSessionEntity? session,
        string? effectiveToolId,
        CancellationToken cancellationToken = default)
    {
        var launchOverride = SessionLaunchOverrideHelper.GetEffectiveOverride(
            SessionLaunchOverrideHelper.Deserialize(session?.ToolLaunchOverridesJson),
            effectiveToolId,
            session?.ToolId,
            session?.CcSwitchSnapshotToolId);

        var overriddenModel = NormalizeValue(launchOverride?.Model);
        var overriddenReasoningEffort = NormalizeValue(launchOverride?.ReasoningEffort);

        if (!string.IsNullOrWhiteSpace(overriddenModel)
            || !string.IsNullOrWhiteSpace(overriddenReasoningEffort)
            || !string.Equals(effectiveToolId, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return (overriddenModel, overriddenReasoningEffort);
        }

        return await ResolveCodexConfigAsync(session?.WorkspacePath, cancellationToken);
    }

    private static async Task<(string? Model, string? ReasoningEffort)> ResolveCodexConfigAsync(
        string? workspacePath,
        CancellationToken cancellationToken)
    {
        var codexDirectory = GetCodexDirectoryPath(workspacePath);
        if (string.IsNullOrWhiteSpace(codexDirectory))
        {
            return (null, null);
        }

        var baseConfigPath = Path.Combine(codexDirectory, "config.toml.base");
        var projectConfigPath = Path.Combine(codexDirectory, "config.toml");
        var configPath = File.Exists(baseConfigPath)
            ? baseConfigPath
            : File.Exists(projectConfigPath)
                ? projectConfigPath
                : null;

        if (string.IsNullOrWhiteSpace(configPath))
        {
            return (null, null);
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken);
        return (
            NormalizeValue(ReadTomlStringSetting(content, "model")),
            NormalizeValue(ReadTomlStringSetting(content, "model_reasoning_effort")));
    }

    private static string? GetCodexDirectoryPath(string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return null;
        }

        var trimmedPath = workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(trimmedPath)
            ? null
            : string.Equals(Path.GetFileName(trimmedPath), ".codex", StringComparison.OrdinalIgnoreCase)
                ? trimmedPath
                : Path.Combine(trimmedPath, ".codex");
    }

    private static string? ReadTomlStringSetting(string content, string key)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var pattern = $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*""(?<value>(?:\\.|[^""])*)""\s*$";
        var match = Regex.Match(content, pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline);
        return match.Success ? UnescapeTomlString(match.Groups["value"].Value) : null;
    }

    private static string? NormalizeValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string UnescapeTomlString(string value)
    {
        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}
