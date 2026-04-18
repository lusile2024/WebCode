using System.Text.Json;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class LocalizationResourceTests
{
    public static TheoryData<string> ZhCnFiles => new()
    {
        @"D:\VSWorkshop\WebCode\WebCodeCli\Resources\Localization\zh-CN.json",
        @"D:\VSWorkshop\WebCode\WebCodeCli\wwwroot\Resources\Localization\zh-CN.json"
    };

    [Theory]
    [MemberData(nameof(ZhCnFiles))]
    public void ZhCnResources_RestoreSetupAndEnvConfigChineseCopy(string filePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(filePath));
        var root = document.RootElement;

        Assert.Equal("cc-switch 状态", GetString(root, "envConfig", "title"));
        Assert.Equal("运行时只读状态", GetString(root, "envConfig", "readOnlyTitle"));
        Assert.Equal("系统初始化 - WebCode", GetString(root, "setup", "pageTitle"));
        Assert.Equal("欢迎使用 WebCode", GetString(root, "setup", "title"));
        Assert.Equal("查看 cc-switch 状态", GetString(root, "setup", "continueToReview"));
        Assert.Equal("新建会话会固定当前 cc-switch 激活 Provider 的 live 配置；已在使用中的会话会保持自己的固定 Provider，只有手动同步后才会切到新的激活项。", GetString(root, "setup", "ccSwitchReviewDesc"));
        Assert.Equal("刷新状态", GetString(root, "setup", "refreshStatus"));
        Assert.Equal("完成初始化", GetString(root, "setup", "completeSetup"));
        Assert.Equal("同步到当前 cc-switch Provider", GetString(root, "codeAssistant", "syncSessionProvider"));
        Assert.Equal("固定 Provider", GetString(root, "codeAssistant", "pinnedProvider"));
        Assert.Equal("同步时间", GetString(root, "codeAssistant", "providerSyncedAt"));
        Assert.Equal("自定义", GetString(root, "setup", "providerCategoryValues", "custom"));
    }

    private static string GetString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            current = current.GetProperty(segment);
        }

        return current.GetString() ?? string.Empty;
    }
}
