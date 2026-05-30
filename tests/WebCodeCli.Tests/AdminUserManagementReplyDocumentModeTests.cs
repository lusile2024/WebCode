using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Controllers;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class AdminUserManagementReplyDocumentModeTests
{
    [Fact]
    public async Task GetFeishuBotConfig_WhenReplyDocumentsAreEnabled_ReturnsDocumentFlags()
    {
        var configService = new AdminControllerReplyDocumentTestsAccessor.StubUserFeishuBotConfigService
        {
            ConfigsByUsername =
            {
                ["alice"] = new UserFeishuBotConfigEntity
                {
                    Username = "alice",
                    IsEnabled = true,
                    FullReplyDocEnabled = true,
                    FinalReplyDocEnabled = true,
                    AudioFullReplyDocEnabled = true,
                    AudioFinalReplyDocEnabled = false
                }
            }
        };

        var controller = AdminControllerReplyDocumentTestsAccessor.CreateController(configService: configService);

        var result = await controller.GetFeishuBotConfig("alice");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserFeishuBotConfigDto>(ok.Value);
        Assert.True(dto.FullReplyDocEnabled);
        Assert.True(dto.FinalReplyDocEnabled);
        Assert.True(dto.AudioFullReplyDocEnabled);
        Assert.False(dto.AudioFinalReplyDocEnabled);
    }

    [Theory]
    [InlineData(ReplyTtsModes.FullReply, true, false)]
    [InlineData(ReplyTtsModes.FinalOnly, false, true)]
    [InlineData(ReplyTtsModes.Off, false, false)]
    public async Task SaveFeishuBotConfig_WhenLegacyModeProvided_MapsToReplyDocumentFlags(
        string mode,
        bool expectedFullReplyDocEnabled,
        bool expectedFinalReplyDocEnabled)
    {
        var configService = new AdminControllerReplyDocumentTestsAccessor.StubUserFeishuBotConfigService();
        var controller = AdminControllerReplyDocumentTestsAccessor.CreateController(configService: configService);

        var result = await controller.SaveFeishuBotConfig("alice", new UserFeishuBotConfigDto
        {
            IsEnabled = true,
            ReplyTtsMode = mode
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(configService.LastSavedConfig);
        Assert.Equal(expectedFullReplyDocEnabled, configService.LastSavedConfig!.FullReplyDocEnabled);
        Assert.Equal(expectedFinalReplyDocEnabled, configService.LastSavedConfig.FinalReplyDocEnabled);
    }

    [Fact]
    public async Task SaveFeishuBotConfig_WhenLegacyReplyTtsEnabled_PrefersFullReplyDocumentCompatibility()
    {
        var configService = new AdminControllerReplyDocumentTestsAccessor.StubUserFeishuBotConfigService();
        var controller = AdminControllerReplyDocumentTestsAccessor.CreateController(configService: configService);

        var result = await controller.SaveFeishuBotConfig("alice", new UserFeishuBotConfigDto
        {
            IsEnabled = true,
            ReplyTtsEnabled = true
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(configService.LastSavedConfig);
        Assert.True(configService.LastSavedConfig!.FullReplyDocEnabled);
        Assert.False(configService.LastSavedConfig.FinalReplyDocEnabled);
    }
}
