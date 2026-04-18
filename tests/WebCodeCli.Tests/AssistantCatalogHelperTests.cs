using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Helpers;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class AssistantCatalogHelperTests
{
    [Fact]
    public void NormalizeEnabledAssistants_KeepsSupportedValuesDistinctAndOrdered()
    {
        var normalized = AssistantCatalogHelper.NormalizeEnabledAssistants(
            ["opencode", "claude-code", "opencode", "unknown", "codex"]);

        Assert.Equal(["claude-code", "codex", "opencode"], normalized);
    }

    [Fact]
    public void FilterAvailableTools_ReturnsAllTools_WhenNoAssistantsAreConfigured()
    {
        var tools = CreateTools();

        var filtered = AssistantCatalogHelper.FilterAvailableTools(tools, []);

        Assert.Equal(["claude-code", "codex", "opencode"], filtered.Select(x => x.Id));
    }

    [Fact]
    public void FilterAvailableTools_ReturnsOnlyEnabledAssistants()
    {
        var tools = CreateTools();

        var filtered = AssistantCatalogHelper.FilterAvailableTools(tools, ["opencode"]);

        Assert.Single(filtered);
        Assert.Equal("opencode", filtered[0].Id);
    }

    private static List<CliToolConfig> CreateTools()
    {
        return
        [
            new CliToolConfig { Id = "claude-code", Name = "Claude Code" },
            new CliToolConfig { Id = "codex", Name = "Codex" },
            new CliToolConfig { Id = "opencode", Name = "OpenCode" }
        ];
    }
}
