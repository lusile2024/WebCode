using System.Text.RegularExpressions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Helpers;

public static partial class LowInterruptionContinueHelper
{
    public static LowInterruptionContinueEligibility Evaluate(
        IReadOnlyList<ChatMessage>? messages,
        bool hasStructuredTodoList,
        bool isToolSupported,
        bool hasCliThreadId,
        bool isProcessRunning)
    {
        if (!isToolSupported || !hasCliThreadId || messages == null || messages.Count == 0)
        {
            return LowInterruptionContinueEligibility.Hidden;
        }

        var latestCompletedAssistant = messages.LastOrDefault(static message =>
            message != null
            && string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
            && message.IsCompleted
            && !message.HasError);

        if (latestCompletedAssistant == null)
        {
            return LowInterruptionContinueEligibility.Hidden;
        }

        var hasUnfinishedWork = hasStructuredTodoList || SessionContainsPlainTextSignal(messages);
        if (!hasUnfinishedWork)
        {
            return LowInterruptionContinueEligibility.Hidden;
        }

        return new LowInterruptionContinueEligibility(
            MessageId: latestCompletedAssistant.Id,
            ShowButton: true,
            IsDisabled: isProcessRunning);
    }

    public static bool IsMessageEligible(ChatMessage? message, LowInterruptionContinueEligibility eligibility)
    {
        if (!eligibility.ShowButton || message == null)
        {
            return false;
        }

        return string.Equals(message.Id, eligibility.MessageId, StringComparison.Ordinal)
            && string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
            && message.IsCompleted
            && !message.HasError;
    }

    private static bool ContainsPlainTextSignal(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return PlainTextSignalRegex().IsMatch(content);
    }

    private static bool SessionContainsPlainTextSignal(IReadOnlyList<ChatMessage> messages)
    {
        return messages.Any(message => message != null && ContainsPlainTextSignal(message.Content));
    }

    [GeneratedRegex(@"\b(plan|backlog|task|todo)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlainTextSignalRegex();
}

public readonly record struct LowInterruptionContinueEligibility(
    string? MessageId,
    bool ShowButton,
    bool IsDisabled)
{
    public static LowInterruptionContinueEligibility Hidden => new(null, false, false);
}
