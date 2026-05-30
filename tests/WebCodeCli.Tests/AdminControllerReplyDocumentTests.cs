using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Controllers;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class AdminControllerReplyDocumentTests
{
    [Fact]
    public async Task GetFeishuBotConfig_ReturnsReplyDocumentFields()
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
                    FinalReplyDocEnabled = false,
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
        Assert.False(dto.FinalReplyDocEnabled);
        Assert.True(dto.AudioFullReplyDocEnabled);
        Assert.False(dto.AudioFinalReplyDocEnabled);
    }

    [Fact]
    public async Task GetFeishuBotConfig_WithoutConfig_ReturnsDefaultReplyDocumentFields()
    {
        var controller = AdminControllerReplyDocumentTestsAccessor.CreateController();

        var result = await controller.GetFeishuBotConfig("bob");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserFeishuBotConfigDto>(ok.Value);
        Assert.Equal("bob", dto.Username);
        Assert.False(dto.FullReplyDocEnabled);
        Assert.False(dto.FinalReplyDocEnabled);
        Assert.False(dto.AudioFullReplyDocEnabled);
        Assert.False(dto.AudioFinalReplyDocEnabled);
    }

    [Fact]
    public async Task SaveFeishuBotConfig_ForwardsReplyDocumentFieldsIntoEntity()
    {
        var configService = new AdminControllerReplyDocumentTestsAccessor.StubUserFeishuBotConfigService();
        var controller = AdminControllerReplyDocumentTestsAccessor.CreateController(configService: configService);

        var result = await controller.SaveFeishuBotConfig("alice", new UserFeishuBotConfigDto
        {
            IsEnabled = true,
            FullReplyDocEnabled = true,
            FinalReplyDocEnabled = true,
            AudioFullReplyDocEnabled = true,
            AudioFinalReplyDocEnabled = true
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(configService.LastSavedConfig);
        Assert.Equal("alice", configService.LastSavedConfig!.Username);
        Assert.True(configService.LastSavedConfig.FullReplyDocEnabled);
        Assert.True(configService.LastSavedConfig.FinalReplyDocEnabled);
        Assert.True(configService.LastSavedConfig.AudioFullReplyDocEnabled);
        Assert.True(configService.LastSavedConfig.AudioFinalReplyDocEnabled);
    }

    [Fact]
    public async Task SaveFeishuBotConfig_WhenLegacyModeIsFullReply_MapsToFullReplyDocument()
    {
        var configService = new AdminControllerReplyDocumentTestsAccessor.StubUserFeishuBotConfigService();
        var controller = AdminControllerReplyDocumentTestsAccessor.CreateController(configService: configService);

        var result = await controller.SaveFeishuBotConfig("alice", new UserFeishuBotConfigDto
        {
            IsEnabled = true,
            ReplyTtsMode = ReplyTtsModes.FullReply
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(configService.LastSavedConfig);
        Assert.True(configService.LastSavedConfig!.FullReplyDocEnabled);
        Assert.False(configService.LastSavedConfig.FinalReplyDocEnabled);
    }

    [Fact]
    public async Task SaveFeishuBotConfig_WhenLegacyModeIsFinalOnly_MapsToFinalReplyDocument()
    {
        var configService = new AdminControllerReplyDocumentTestsAccessor.StubUserFeishuBotConfigService();
        var controller = AdminControllerReplyDocumentTestsAccessor.CreateController(configService: configService);

        var result = await controller.SaveFeishuBotConfig("alice", new UserFeishuBotConfigDto
        {
            IsEnabled = true,
            ReplyTtsMode = ReplyTtsModes.FinalOnly
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(configService.LastSavedConfig);
        Assert.False(configService.LastSavedConfig!.FullReplyDocEnabled);
        Assert.True(configService.LastSavedConfig.FinalReplyDocEnabled);
    }

    [Fact]
    public async Task SaveFeishuBotConfig_WhenLegacyReplyTtsEnabled_MapsToFullReplyDocument()
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
