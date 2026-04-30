using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Domain.Tests;

public class SuperpowersPromptBuilderTests
{
    [Fact]
    public void BuildExecutePlanPrompt_ReturnsApprovedPrompt()
    {
        Assert.Equal(
            "使用superpowers的executing-plans技能执行计划",
            SuperpowersPromptBuilder.BuildExecutePlanPrompt());
    }

    [Fact]
    public void BuildSubagentExecutePlanPrompt_ReturnsApprovedCombinedPrompt()
    {
        Assert.Equal(
            "使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能",
            SuperpowersPromptBuilder.BuildSubagentExecutePlanPrompt());
    }

    [Theory]
    [InlineData("写一个执行步骤", "使用superpowers技能，写一个执行步骤")]
    [InlineData("使用superpowers技能，写一个执行步骤", "使用superpowers技能，写一个执行步骤")]
    [InlineData("  写一个执行步骤  ", "使用superpowers技能，写一个执行步骤")]
    public void BuildQuickSkillPrompt_AppliesPrefixOnlyWhenMissing(string input, string expected)
    {
        Assert.Equal(expected, SuperpowersPromptBuilder.BuildQuickSkillPrompt(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildQuickSkillPrompt_ReturnsNullForBlankInput(string? input)
    {
        Assert.Null(SuperpowersPromptBuilder.BuildQuickSkillPrompt(input));
    }
}
