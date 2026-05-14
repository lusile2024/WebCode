namespace WebCodeCli.Domain.Domain.Service.Adapters;

public class CliExecutionRequest
{
    public string SessionId { get; set; } = string.Empty;

    public string ToolId { get; set; } = string.Empty;

    public string PromptText { get; set; } = string.Empty;

    public CliSessionContext SessionContext { get; set; } = new();

    public List<CliExecutionAttachment> NativeAttachments { get; set; } = new();

    public List<CliExecutionAttachment> ReferenceAttachments { get; set; } = new();
}
