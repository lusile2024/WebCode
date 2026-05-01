namespace WebCodeCli.Domain.Domain.Model;

public static class LowInterruptionContinueDefaults
{
    public const string PromptFieldName = "low_interruption_prompt";
    public const string DefaultPrompt = "Keep executing continuously with minimal interruption until the plan, backlog, and all tasks are completed.";
    public const string PromptPlaceholder = "可按需补充本次少打断执行目标";
}
