using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Repositories.Base.UserFeishuBotConfig;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class UserFeishuBotOptionsFactoryTests
{
    [Fact]
    public void CreateSharedDefaults_ClearsCredentialsAndKeepsDisplayDefaults()
    {
        var globalOptions = new FeishuOptions
        {
            Enabled = true,
            AppId = "cli_global",
            AppSecret = "global-secret",
            EncryptKey = "global-encrypt",
            VerificationToken = "global-token",
            DefaultCardTitle = "Global Bot",
            ThinkingMessage = "thinking...",
            HttpTimeoutSeconds = 45,
            StreamingThrottleMs = 900
        };

        var result = UserFeishuBotOptionsFactory.CreateSharedDefaults(globalOptions);

        Assert.True(result.Enabled);
        Assert.Equal(string.Empty, result.AppId);
        Assert.Equal(string.Empty, result.AppSecret);
        Assert.Equal(string.Empty, result.EncryptKey);
        Assert.Equal(string.Empty, result.VerificationToken);
        Assert.Equal("Global Bot", result.DefaultCardTitle);
        Assert.Equal("thinking...", result.ThinkingMessage);
        Assert.Equal(45, result.HttpTimeoutSeconds);
        Assert.Equal(900, result.StreamingThrottleMs);
    }

    [Fact]
    public void CreateEffectiveOptions_ReturnsNull_WhenConfigIsDisabled()
    {
        var defaults = UserFeishuBotOptionsFactory.CreateSharedDefaults(new FeishuOptions());
        var config = new UserFeishuBotConfigEntity
        {
            Username = "alice",
            IsEnabled = false,
            AppId = "cli_alice",
            AppSecret = "alice-secret"
        };

        var result = UserFeishuBotOptionsFactory.CreateEffectiveOptions(defaults, config);

        Assert.Null(result);
    }

    [Fact]
    public void CreateEffectiveOptions_MergesUserCredentialsAndOptionalOverrides()
    {
        var defaults = UserFeishuBotOptionsFactory.CreateSharedDefaults(new FeishuOptions
        {
            Enabled = true,
            DefaultCardTitle = "Shared Title",
            ThinkingMessage = "Shared Thinking",
            HttpTimeoutSeconds = 30,
            StreamingThrottleMs = 500
        });

        var config = new UserFeishuBotConfigEntity
        {
            Username = "alice",
            IsEnabled = true,
            AppId = " cli_alice ",
            AppSecret = " alice-secret ",
            EncryptKey = " encrypt ",
            VerificationToken = " verify ",
            DefaultCardTitle = " Alice Bot ",
            ThinkingMessage = " Alice Thinking ",
            HttpTimeoutSeconds = 99,
            StreamingThrottleMs = 1200
        };

        var result = UserFeishuBotOptionsFactory.CreateEffectiveOptions(defaults, config);

        Assert.NotNull(result);
        Assert.Equal("cli_alice", result!.AppId);
        Assert.Equal("alice-secret", result.AppSecret);
        Assert.Equal("encrypt", result.EncryptKey);
        Assert.Equal("verify", result.VerificationToken);
        Assert.Equal("Alice Bot", result.DefaultCardTitle);
        Assert.Equal("Alice Thinking", result.ThinkingMessage);
        Assert.Equal(99, result.HttpTimeoutSeconds);
        Assert.Equal(1200, result.StreamingThrottleMs);
    }

    [Fact]
    public void ShouldTrackExecuteTask_ReturnsFalse_ForFeishuWebSocketHostedService()
    {
        var result = HostedServiceRuntimeMonitorPolicy.ShouldTrackExecuteTask(
            HostedServiceRuntimeMonitorPolicy.FeishuWebSocketHostedServiceType,
            isBackgroundService: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldTrackExecuteTask_ReturnsTrue_ForRegularBackgroundService()
    {
        var result = HostedServiceRuntimeMonitorPolicy.ShouldTrackExecuteTask(
            "MyApp.Services.WorkerService",
            isBackgroundService: true);

        Assert.True(result);
    }
}
