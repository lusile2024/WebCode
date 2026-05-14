using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service.Adapters;

namespace WebCodeCli.Domain.Tests;

public class CliExecutionRequestAdapterTests
{
    [Fact]
    public void CodexAdapter_BuildArguments_RequestOverload_EncodesNativeImagesAndReferencePreamble()
    {
        var adapter = new CodexAdapter();
        var tool = new CliToolConfig
        {
            Id = "codex",
            Name = "Codex",
            Command = "codex",
            Enabled = true
        };
        var request = new CliExecutionRequest
        {
            SessionId = "session-123",
            ToolId = "codex",
            PromptText = "Review the staged files.",
            SessionContext = new CliSessionContext
            {
                SessionId = "session-123",
                WorkingDirectory = Path.GetTempPath()
            },
            NativeAttachments =
            [
                new CliExecutionAttachment
                {
                    DisplayName = "diagram.png",
                    Kind = MessageAttachmentKind.Image,
                    AbsolutePath = @"D:\attachments\diagram.png",
                    WorkspaceRelativePath = ".webcode/message-inputs/submission-1/diagram.png"
                }
            ],
            ReferenceAttachments =
            [
                new CliExecutionAttachment
                {
                    DisplayName = "notes.pdf",
                    Kind = MessageAttachmentKind.Pdf,
                    AbsolutePath = @"D:\attachments\notes.pdf",
                    WorkspaceRelativePath = ".webcode/message-inputs/submission-1/notes.pdf"
                }
            ]
        };

        var arguments = adapter.BuildArguments(tool, request);

        Assert.Contains("-i \"D:\\attachments\\diagram.png\"", arguments, StringComparison.Ordinal);
        Assert.Contains("[WEBCODE_REFERENCE_ATTACHMENTS]", arguments, StringComparison.Ordinal);
        Assert.Contains(".webcode/message-inputs/submission-1/notes.pdf", arguments, StringComparison.Ordinal);
        Assert.Contains("Review the staged files.", arguments, StringComparison.Ordinal);
    }
}
