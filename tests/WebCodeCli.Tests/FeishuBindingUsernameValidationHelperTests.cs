using WebCodeCli.Domain.Domain.Service;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class FeishuBindingUsernameValidationHelperTests
{
    [Fact]
    public void Validate_ReturnsFailure_WhenUserDoesNotExist()
    {
        var result = FeishuBindingUsernameValidationHelper.Validate("alice", null, null);

        Assert.False(result.Success);
        Assert.Equal("Web 用户不存在: alice", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ReturnsFailure_WhenUsernameDoesNotExactlyMatchConfiguredUsername()
    {
        var result = FeishuBindingUsernameValidationHelper.Validate("Alice", "alice", null);

        Assert.False(result.Success);
        Assert.Equal("绑定用户名必须与用户管理中配置的用户名完全一致：alice", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ReturnsFailure_WhenBotOwnerUsernameDoesNotMatch()
    {
        var result = FeishuBindingUsernameValidationHelper.Validate("alice", "alice", "bob");

        Assert.False(result.Success);
        Assert.Equal("当前飞书机器人仅允许绑定用户：bob", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ReturnsSuccess_WhenUsernameExactlyMatchesBotOwner()
    {
        var result = FeishuBindingUsernameValidationHelper.Validate("alice", "alice", "alice");

        Assert.True(result.Success);
        Assert.Equal("alice", result.WebUsername);
    }
}
