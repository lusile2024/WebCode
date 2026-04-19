using System.Text.RegularExpressions;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Adapters;

namespace WebCodeCli.Domain.Domain.Service.Channels;

internal static partial class LowInterruptionContinueHelper
{
    public const string ActionName = "low_interruption_continue";
    public const string ButtonLabel = "少打断执行";

    public static bool HasStructuredTodoList(CliOutputEvent? outputEvent)
    {
        return outputEvent != null
               && string.Equals(outputEvent.ItemType, "todo_list", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasPlainTextSignal(string? content)
    {
        return !string.IsNullOrWhiteSpace(content)
               && PlainTextSignalRegex().IsMatch(content);
    }

    public static bool IsEligible(
        string? content,
        bool hasStructuredTodoList,
        bool isToolSupported,
        bool hasCliThreadId,
        bool isProcessRunning)
    {
        return isToolSupported
               && hasCliThreadId
               && !isProcessRunning
               && (hasStructuredTodoList || HasPlainTextSignal(content));
    }

    public static FeishuStreamingCardBottomAction CreateBottomAction(string sessionId, string chatKey, string toolId)
    {
        return new FeishuStreamingCardBottomAction
        {
            Text = ButtonLabel,
            Type = "primary",
            Value = new
            {
                action = ActionName,
                session_id = sessionId,
                chat_key = chatKey,
                tool_id = toolId
            }
        };
    }

    [GeneratedRegex(@"\b(plan|backlog)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlainTextSignalRegex();
}
