using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Domain.Tests;

public sealed class GoalPromptBuilderTests
{
    [Theory]
    [InlineData("整理这个目标", "/goal 整理这个目标")]
    [InlineData("/goal 整理这个目标", "/goal 整理这个目标")]
    [InlineData("  整理这个目标  ", "/goal 整理这个目标")]
    public void BuildGoalPrompt_AppliesPrefixOnlyWhenMissing(string input, string expected)
    {
        Assert.Equal(expected, GoalPromptBuilder.BuildGoalPrompt(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildGoalPrompt_ReturnsNullForBlankInput(string? input)
    {
        Assert.Null(GoalPromptBuilder.BuildGoalPrompt(input));
    }

    [Fact]
    public void BuildSubagentPlanGoalPrompt_RequiresClosingAllPlanChecklistItemsBeforeGoalCompletion()
    {
        var prompt = GoalPromptBuilder.BuildSubagentPlanGoalPrompt();

        Assert.StartsWith("/goal ", prompt, StringComparison.Ordinal);
        Assert.Contains("plan文档内的[ ]check list都检查收口后，变成[x]后才算goal完成", prompt, StringComparison.Ordinal);
    }
}
