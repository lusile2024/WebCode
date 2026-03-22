using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class FeishuBotAppIdOwnershipHelperTests
{
    [Fact]
    public void FindConflictingUsername_ReturnsExistingOwnerForAnotherUser()
    {
        var configs = new[]
        {
            new UserFeishuBotConfigEntity { Username = "alice", AppId = " CLI_A929 " },
            new UserFeishuBotConfigEntity { Username = "bob", AppId = "cli_other" }
        };

        var result = FeishuBotAppIdOwnershipHelper.FindConflictingUsername("charlie", "cli_a929", configs);

        Assert.Equal("alice", result);
    }

    [Fact]
    public void FindConflictingUsername_IgnoresCurrentUsersOwnConfig()
    {
        var configs = new[]
        {
            new UserFeishuBotConfigEntity { Username = "Alice ", AppId = " CLI_A929 " }
        };

        var result = FeishuBotAppIdOwnershipHelper.FindConflictingUsername(" alice", "cli_a929", configs);

        Assert.Null(result);
    }

    [Fact]
    public void FindConflictingUsername_IgnoresBlankAppIds()
    {
        var configs = new[]
        {
            new UserFeishuBotConfigEntity { Username = "alice", AppId = "   " },
            new UserFeishuBotConfigEntity { Username = "bob", AppId = null }
        };

        var result = FeishuBotAppIdOwnershipHelper.FindConflictingUsername("charlie", " ", configs);

        Assert.Null(result);
    }
}
