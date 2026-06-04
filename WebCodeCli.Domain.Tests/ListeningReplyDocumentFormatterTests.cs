using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public sealed class ListeningReplyDocumentFormatterTests
{
    [Fact]
    public void Format_ReplacesDistinctFileReferencesWithSequentialPlaceholders()
    {
        const string input = "构建过了。当前主要是仓库里原有警告，还包括 /D:/VSWorkshop/WebCode/WebCodeCli/Pages/SharedWorkspace.razor:812、/D:/VSWorkshop/WebCode/WebCodeCli/Pages/SharedSession.razor:241。";

        var output = ListeningReplyDocumentFormatter.Format(input);

        Assert.Contains("文件内容1", output, StringComparison.Ordinal);
        Assert.Contains("文件内容2", output, StringComparison.Ordinal);
        Assert.Contains("文件内容1：/D:/VSWorkshop/WebCode/WebCodeCli/Pages/SharedWorkspace.razor:812", output, StringComparison.Ordinal);
        Assert.Contains("文件内容2：/D:/VSWorkshop/WebCode/WebCodeCli/Pages/SharedSession.razor:241", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ReusesPlaceholderForRepeatedFileReference()
    {
        const string input = "先看 /D:/repo/a.cs:1，再看 /D:/repo/a.cs:1。";

        var output = ListeningReplyDocumentFormatter.Format(input);

        Assert.Contains("文件内容1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("文件内容2", output, StringComparison.Ordinal);
        var bodyOnly = output.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.None)[0];
        Assert.Equal(
            2,
            bodyOnly.Split("文件内容1", StringSplitOptions.None).Length - 1);
        Assert.Contains("文件内容1：/D:/repo/a.cs:1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_LeavesPlainTextUntouched_WhenNoFileReferenceExists()
    {
        const string input = "构建过了。当前主要是仓库里原有警告。";

        var output = ListeningReplyDocumentFormatter.Format(input);

        Assert.Equal(input, output);
    }

    [Fact]
    public void Format_ReplacesRelativePathAndBareFileNames_InMarkdownLinkText()
    {
        const string input = """
Package C 这次已经补到AI 可直接使用的层了。

新增了专用技能和本地脚本：
[mmis-page-metadata-operations/skill.md](<文件内容2>)
[page-metadata-ops.ps1](<文件内容3>)

同时把主流程技能导流到了这个专用入口：
[authoring-first-automation.md](<文件内容4>)
[2026-05-28-mmis-page-metadata-full-stack-rearchitecture-implementation-plan.md](<文件内容5>)
""";

        var output = ListeningReplyDocumentFormatter.Format(input);
        var bodyOnly = output.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.None)[0];

        Assert.DoesNotContain("mmis-page-metadata-operations/skill.md", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("page-metadata-ops.ps1", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("authoring-first-automation.md", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-05-28-mmis-page-metadata-full-stack-rearchitecture-implementation-plan.md", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("[文件内容1](<文件内容2>)", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("[文件内容2](<文件内容3>)", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("[文件内容3](<文件内容4>)", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("[文件内容4](<文件内容5>)", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("文件内容1：mmis-page-metadata-operations/skill.md", output, StringComparison.Ordinal);
        Assert.Contains("文件内容2：page-metadata-ops.ps1", output, StringComparison.Ordinal);
        Assert.Contains("文件内容3：authoring-first-automation.md", output, StringComparison.Ordinal);
        Assert.Contains("文件内容4：2026-05-28-mmis-page-metadata-full-stack-rearchitecture-implementation-plan.md", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ReplacesWindowsAndUnixStyleRelativePaths_AsSingleFileReference()
    {
        const string input = @"请检查 WmsServerV4\src\Cimc.Tianda.Wms.Application\1397\Custom\MoveOut\DeliveringBillServiceCus1397.cs 和 docs/agent-notes/2026-05-29.md。";

        var output = ListeningReplyDocumentFormatter.Format(input);
        var bodyOnly = output.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.None)[0];

        Assert.DoesNotContain(@"WmsServerV4\src\Cimc.Tianda.Wms.Application\1397\Custom\MoveOut\DeliveringBillServiceCus1397.cs", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/agent-notes/2026-05-29.md", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("文件内容1", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("文件内容2", bodyOnly, StringComparison.Ordinal);
        Assert.Contains(@"文件内容1：WmsServerV4\src\Cimc.Tianda.Wms.Application\1397\Custom\MoveOut\DeliveringBillServiceCus1397.cs", output, StringComparison.Ordinal);
        Assert.Contains("文件内容2：docs/agent-notes/2026-05-29.md", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_DoesNotRewrite_UrlsEmailsOrVersions()
    {
        const string input = "Reference https://open.feishu.cn/document/server-path, email alice@example.com, version v1.2.3, and file docs/agent-notes/2026-05-29.md.";

        var output = ListeningReplyDocumentFormatter.Format(input);
        var parts = output.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.None);
        var bodyOnly = parts[0];
        var appendixOnly = parts[1];

        Assert.Contains("https://open.feishu.cn/document/server-path", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("alice@example.com", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("v1.2.3", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("open.feishu.cn", appendixOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", appendixOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("v1.2.3", appendixOnly, StringComparison.Ordinal);
        Assert.Contains("文件内容1：docs/agent-notes/2026-05-29.md", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ReplacesFencedPowerShellBlock_WithCommandPlaceholderAndAppendsCommandsAtEnd()
    {
        const string input = """
Actual verification commands:
```powershell
powershell -NoProfile -File MMIS-Server/scripts/page-metadata-ops.ps1 -Operation publish -PageCode system/user -ProjectCode MMIS -Username mmis.admin
powershell -NoProfile -File MMIS-Server/scripts/page-metadata-ops.ps1 -Operation verify -PageCode system/user -ProjectCode MMIS -Username mmis.admin
```
The document still references page-metadata-ops.ps1 outside the command block.
""";

        var output = ListeningReplyDocumentFormatter.Format(input);
        var parts = output.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.None);
        var bodyOnly = parts[0];
        var appendixOnly = string.Join(Environment.NewLine + Environment.NewLine, parts.Skip(1));

        Assert.Contains("[命令内容1]", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("```powershell", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("-Operation publish", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("文件内容1", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("命令内容1：powershell -NoProfile -File MMIS-Server/scripts/page-metadata-ops.ps1 -Operation publish -PageCode system/user -ProjectCode MMIS -Username mmis.admin", appendixOnly, StringComparison.Ordinal);
        Assert.Contains("powershell -NoProfile -File MMIS-Server/scripts/page-metadata-ops.ps1 -Operation verify -PageCode system/user -ProjectCode MMIS -Username mmis.admin", appendixOnly, StringComparison.Ordinal);
        Assert.Contains("文件内容1：page-metadata-ops.ps1", appendixOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_LeavesInlineBackticksUntouched_WhenTheyAreNotFencedCommands()
    {
        const string input = "Use `page-metadata-ops.ps1` manually and see docs/agent-notes/2026-05-29.md for details.";

        var output = ListeningReplyDocumentFormatter.Format(input);
        var parts = output.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.None);
        var bodyOnly = parts[0];
        var appendixOnly = parts[1];

        Assert.DoesNotContain("[命令内容", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("`文件内容1`", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("文件内容2", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("命令内容", appendixOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ReplacesMarkdownListCommandItems_WithCommandPlaceholdersAndAppendsCommandsAtEnd()
    {
        const string input = """
**验证**
- `dotnet build WebCodeCli.sln --no-restore -v minimal`
- `dotnet test WebCodeCli.Domain.Tests/WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~ListeningReplyDocumentFormatterTests"`

这几项现在都通过了。
""";

        var output = ListeningReplyDocumentFormatter.Format(input);
        var parts = output.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.None);
        var bodyOnly = parts[0];
        var appendixOnly = string.Join(Environment.NewLine + Environment.NewLine, parts.Skip(1));

        Assert.Contains("- [命令内容1]", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("- [命令内容2]", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build", bodyOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("命令内容1：dotnet build WebCodeCli.sln --no-restore -v minimal", appendixOnly, StringComparison.Ordinal);
        Assert.Contains("命令内容2：dotnet test WebCodeCli.Domain.Tests/WebCodeCli.Domain.Tests.csproj --filter \"FullyQualifiedName~ListeningReplyDocumentFormatterTests\"", appendixOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ReplacesStandaloneCommandLines_WithoutTouchingInlineCommandProse()
    {
        const string input = """
验证命令如下：
dotnet build WebCodeCli.sln --no-restore -v minimal
powershell -NoProfile -File scripts/check.ps1

运行 `dotnet build` 即可看到结果说明。
""";

        var output = ListeningReplyDocumentFormatter.Format(input);
        var parts = output.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.None);
        var bodyOnly = parts[0];
        var appendixOnly = string.Join(Environment.NewLine + Environment.NewLine, parts.Skip(1));

        Assert.Contains("[命令内容1]", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("[命令内容2]", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("运行 `dotnet build` 即可看到结果说明。", bodyOnly, StringComparison.Ordinal);
        Assert.Contains("命令内容1：dotnet build WebCodeCli.sln --no-restore -v minimal", appendixOnly, StringComparison.Ordinal);
        Assert.Contains("命令内容2：powershell -NoProfile -File scripts/check.ps1", appendixOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_LeavesNonCommandFencedCodeBlockUntouched()
    {
        const string input = """
Reference snippet:
```csharp
Console.WriteLine("hello");
```
""";

        var output = ListeningReplyDocumentFormatter.Format(input);

        Assert.Equal(input, output);
    }
}
