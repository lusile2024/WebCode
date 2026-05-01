using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Helpers;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class SuperpowersQuickActionHelperTests
{
    [Fact]
    public void Evaluate_ShowsQuickInputButHidesPlanActions_WhenWorkspaceHasNoPlanFiles()
    {
        var messages = new List<ChatMessage>
        {
            CreateUser("使用superpowers技能，先看一下计划"),
            CreateAssistant("a1", "好的，先看一下。")
        };

        var result = SuperpowersQuickActionHelper.Evaluate(
            messages,
            hasSuperpowersPlanFiles: false,
            isProcessRunning: false);

        Assert.True(result.ShowQuickInput);
        Assert.False(result.ShowPlanActions);
        Assert.Equal("a1", result.MessageId);
    }

    [Fact]
    public void Evaluate_ShowsQuickInputButHidesPlanActions_WhenSessionHistoryHasNoSuperpowersSignal()
    {
        var messages = new List<ChatMessage>
        {
            CreateUser("先看一下计划"),
            CreateAssistant("a1", "好的，先看一下。")
        };

        var result = SuperpowersQuickActionHelper.Evaluate(
            messages,
            hasSuperpowersPlanFiles: true,
            isProcessRunning: false);

        Assert.True(result.ShowQuickInput);
        Assert.False(result.ShowPlanActions);
        Assert.Equal("a1", result.MessageId);
    }

    [Fact]
    public void Evaluate_TargetsLatestCompletedAssistant_WhenPlanFilesExistAndHistoryContainsSuperpowers()
    {
        var firstAssistant = CreateAssistant("a1", "先完成第一部分。");
        var latestAssistant = CreateAssistant("a2", "第二部分也准备好了。");
        var messages = new List<ChatMessage>
        {
            CreateUser("使用superpowers技能，先看一下计划"),
            firstAssistant,
            CreateUser("继续"),
            latestAssistant
        };

        var result = SuperpowersQuickActionHelper.Evaluate(
            messages,
            hasSuperpowersPlanFiles: true,
            isProcessRunning: false);

        Assert.True(result.ShowQuickInput);
        Assert.True(result.ShowPlanActions);
        Assert.False(result.IsDisabled);
        Assert.Equal(latestAssistant.Id, result.MessageId);
        Assert.True(SuperpowersQuickActionHelper.IsMessageEligible(latestAssistant, result));
        Assert.False(SuperpowersQuickActionHelper.IsMessageEligible(firstAssistant, result));
    }

    [Fact]
    public void Evaluate_DisablesQuickActions_WhileProcessIsRunning()
    {
        var latestAssistant = CreateAssistant("a1", "已经进入执行阶段。");

        var result = SuperpowersQuickActionHelper.Evaluate(
            [CreateUser("superpowers"), latestAssistant],
            hasSuperpowersPlanFiles: true,
            isProcessRunning: true);

        Assert.True(result.ShowQuickInput);
        Assert.True(result.ShowPlanActions);
        Assert.True(result.IsDisabled);
        Assert.Equal(latestAssistant.Id, result.MessageId);
    }

    private static ChatMessage CreateUser(string content)
    {
        return new ChatMessage
        {
            Role = "user",
            Content = content,
            IsCompleted = true
        };
    }

    private static ChatMessage CreateAssistant(string id, string content)
    {
        return new ChatMessage
        {
            Id = id,
            Role = "assistant",
            Content = content,
            IsCompleted = true
        };
    }
}
