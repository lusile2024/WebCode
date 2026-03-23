using Microsoft.Extensions.Logging.Abstractions;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public class FeishuCommandServiceTests
{
    [Theory]
    [InlineData("claude-code")]
    [InlineData("codex")]
    [InlineData("opencode")]
    public async Task GetCommandsAsync_IncludesHistoryBuiltInCommand(string toolId)
    {
        var service = new FeishuCommandService(
            NullLogger<FeishuCommandService>.Instance,
            new CommandScannerService());

        var commands = await service.GetCommandsAsync(toolId);

        var historyCommand = Assert.Single(commands.Where(command => command.Id == "history"));
        Assert.Equal("/history", historyCommand.Name);
        Assert.Equal("/history", historyCommand.ExecuteText);
    }
}
