using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Helpers;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class LowInterruptionContinueHelperTests
{
    [Fact]
    public void Evaluate_ReturnsLatestCompletedAssistant_WhenStructuredTodoListExists()
    {
        var firstAssistant = CreateAssistant("first", "已完成阶段一");
        var latestAssistant = CreateAssistant("latest", string.Empty);
        var messages = new List<ChatMessage>
        {
            CreateUser("帮我继续"),
            firstAssistant,
            CreateUser("再看下"),
            latestAssistant
        };

        var result = LowInterruptionContinueHelper.Evaluate(
            messages,
            hasStructuredTodoList: true,
            isToolSupported: true,
            hasCliThreadId: true,
            isProcessRunning: false);

        Assert.True(result.ShowButton);
        Assert.False(result.IsDisabled);
        Assert.Equal(latestAssistant.Id, result.MessageId);
        Assert.True(LowInterruptionContinueHelper.IsMessageEligible(latestAssistant, result));
        Assert.False(LowInterruptionContinueHelper.IsMessageEligible(firstAssistant, result));
    }

    [Theory]
    [InlineData("Plan:\n1. finish api\n2. update ui")]
    [InlineData("BACKLOG\n- fix failing tests")]
    [InlineData("TODO\n- keep going")]
    [InlineData("task: continue migration cleanup")]
    public void Evaluate_UsesTextFallback_WhenLatestAssistantContainsPlanOrBacklog(string content)
    {
        var latestAssistant = CreateAssistant("latest", content);

        var result = LowInterruptionContinueHelper.Evaluate(
            [CreateUser("继续"), latestAssistant],
            hasStructuredTodoList: false,
            isToolSupported: true,
            hasCliThreadId: true,
            isProcessRunning: false);

        Assert.True(result.ShowButton);
        Assert.False(result.IsDisabled);
        Assert.Equal(latestAssistant.Id, result.MessageId);
    }

    [Theory]
    [InlineData("我继续沿着 backlog 往下推")]
    [InlineData("please make a small plan and keep going")]
    [InlineData("task list stays open")]
    public void Evaluate_UsesSessionSignal_WhenAssistantDoesNotRepeatKeywords(string userContent)
    {
        var latestAssistant = CreateAssistant("latest", "这次回复只汇报执行结果，不重复关键词。");

        var result = LowInterruptionContinueHelper.Evaluate(
            [CreateUser(userContent), latestAssistant],
            hasStructuredTodoList: false,
            isToolSupported: true,
            hasCliThreadId: true,
            isProcessRunning: false);

        Assert.True(result.ShowButton);
        Assert.False(result.IsDisabled);
        Assert.Equal(latestAssistant.Id, result.MessageId);
    }

    [Fact]
    public void Evaluate_UsesOlderSessionSignalsAcrossWholeConversation()
    {
        var olderAssistant = CreateAssistant("older", "Backlog:\n- old unfinished work");
        var latestAssistant = CreateAssistant("latest", "这次回复已经收尾了");

        var result = LowInterruptionContinueHelper.Evaluate(
            [CreateUser("第一次"), olderAssistant, CreateUser("第二次"), latestAssistant],
            hasStructuredTodoList: false,
            isToolSupported: true,
            hasCliThreadId: true,
            isProcessRunning: false);

        Assert.True(result.ShowButton);
        Assert.Equal(latestAssistant.Id, result.MessageId);
        Assert.False(LowInterruptionContinueHelper.IsMessageEligible(olderAssistant, result));
    }

    [Fact]
    public void Evaluate_HidesButton_WhenToolDoesNotSupportLowInterruption()
    {
        var latestAssistant = CreateAssistant("latest", "Plan:\n- continue working");

        var result = LowInterruptionContinueHelper.Evaluate(
            [CreateUser("继续"), latestAssistant],
            hasStructuredTodoList: false,
            isToolSupported: false,
            hasCliThreadId: true,
            isProcessRunning: false);

        Assert.False(result.ShowButton);
        Assert.Null(result.MessageId);
    }

    [Fact]
    public void Evaluate_HidesButton_WhenCliThreadIsMissing()
    {
        var latestAssistant = CreateAssistant("latest", "Backlog:\n- continue working");

        var result = LowInterruptionContinueHelper.Evaluate(
            [CreateUser("继续"), latestAssistant],
            hasStructuredTodoList: false,
            isToolSupported: true,
            hasCliThreadId: false,
            isProcessRunning: false);

        Assert.False(result.ShowButton);
        Assert.Null(result.MessageId);
    }

    [Fact]
    public void Evaluate_DisablesButton_WhenProcessIsAlreadyRunning()
    {
        var latestAssistant = CreateAssistant("latest", "Plan:\n- continue working");

        var result = LowInterruptionContinueHelper.Evaluate(
            [CreateUser("继续"), latestAssistant],
            hasStructuredTodoList: false,
            isToolSupported: true,
            hasCliThreadId: true,
            isProcessRunning: true);

        Assert.True(result.ShowButton);
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
