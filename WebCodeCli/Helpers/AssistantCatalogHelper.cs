using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Helpers;

public static class AssistantCatalogHelper
{
    public const string ClaudeCodeId = "claude-code";
    public const string CodexId = "codex";
    public const string OpenCodeId = "opencode";

    private static readonly AssistantDescriptor[] SupportedAssistantsInternal =
    [
        new(ClaudeCodeId, "Claude Code", "claudeCodeDesc"),
        new(CodexId, "Codex", "codexDesc"),
        new(OpenCodeId, "OpenCode", "openCodeDesc")
    ];

    public static IReadOnlyList<AssistantDescriptor> SupportedAssistants => SupportedAssistantsInternal;

    public static IReadOnlyList<string> SupportedAssistantIds =>
        SupportedAssistantsInternal.Select(x => x.Id).ToArray();

    public static List<string> NormalizeEnabledAssistants(IEnumerable<string>? assistantIds)
    {
        var selected = new HashSet<string>(
            assistantIds?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
            ?? [],
            StringComparer.OrdinalIgnoreCase);

        return SupportedAssistantsInternal
            .Where(x => selected.Contains(x.Id))
            .Select(x => x.Id)
            .ToList();
    }

    public static List<CliToolConfig> FilterAvailableTools(
        IEnumerable<CliToolConfig> allTools,
        IEnumerable<string>? enabledAssistants)
    {
        var toolList = allTools.ToList();
        var normalizedAssistants = NormalizeEnabledAssistants(enabledAssistants);

        if (normalizedAssistants.Count == 0)
        {
            return toolList;
        }

        return toolList
            .Where(tool => normalizedAssistants.Any(assistantId => MatchesAssistant(tool, assistantId)))
            .ToList();
    }

    public static bool MatchesAssistant(CliToolConfig tool, string assistantId)
    {
        if (tool == null || string.IsNullOrWhiteSpace(assistantId))
        {
            return false;
        }

        return assistantId switch
        {
            ClaudeCodeId => tool.Id.Contains("claude", StringComparison.OrdinalIgnoreCase)
                || tool.Id.Equals(ClaudeCodeId, StringComparison.OrdinalIgnoreCase),
            CodexId => tool.Id.Contains("codex", StringComparison.OrdinalIgnoreCase)
                || tool.Id.Equals(CodexId, StringComparison.OrdinalIgnoreCase),
            OpenCodeId => tool.Id.Contains("opencode", StringComparison.OrdinalIgnoreCase)
                || tool.Id.Equals(OpenCodeId, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public sealed record AssistantDescriptor(string Id, string DisplayName, string DescriptionKey);
}
