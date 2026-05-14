using WebCodeCli.Domain.Domain.Service.Adapters;

namespace WebCodeCli.Domain.Domain.Model;

public class PreparedMessageSubmission
{
    public ChatMessage UserMessage { get; set; } = new();

    public CliExecutionRequest ExecutionRequest { get; set; } = new();

    public List<MessageSubmissionWarning> Warnings { get; set; } = new();
}
