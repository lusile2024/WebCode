namespace WebCodeCli.Domain.Common;

internal static class CliThreadIdRecoveryHelper
{
    public static string? TryRecoverFromImportedTitle(string? toolId, string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalizedToolId = NormalizeToolId(toolId);
        var prefix = normalizedToolId switch
        {
            "codex" => "[Codex] ",
            "claude-code" => "[Claude Code] ",
            "opencode" => "[OpenCode] ",
            _ => null
        };

        if (string.IsNullOrWhiteSpace(prefix) || !title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var candidate = title[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.EndsWith("...", StringComparison.Ordinal))
        {
            return null;
        }

        return IsLikelyCliThreadId(normalizedToolId, candidate)
            ? candidate
            : null;
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

    private static bool IsLikelyCliThreadId(string normalizedToolId, string candidate)
    {
        if (Guid.TryParse(candidate, out _))
        {
            return true;
        }

        return normalizedToolId == "opencode"
               && candidate.StartsWith("ses_", StringComparison.OrdinalIgnoreCase);
    }
}
