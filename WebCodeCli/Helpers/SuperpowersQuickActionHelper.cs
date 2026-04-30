using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Helpers;

public static class SuperpowersQuickActionHelper
{
    public static SuperpowersQuickActionEligibility Evaluate(
        IReadOnlyList<ChatMessage>? messages,
        bool hasSuperpowersPlanFiles,
        bool isProcessRunning)
    {
        if (!hasSuperpowersPlanFiles || messages == null || messages.Count == 0 || !SessionContainsSuperpowers(messages))
        {
            return SuperpowersQuickActionEligibility.Hidden;
        }

        var latestCompletedAssistant = messages.LastOrDefault(static message =>
            message != null
            && string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
            && message.IsCompleted
            && !message.HasError);

        if (latestCompletedAssistant == null)
        {
            return SuperpowersQuickActionEligibility.Hidden;
        }

        return new SuperpowersQuickActionEligibility(
            MessageId: latestCompletedAssistant.Id,
            ShowQuickActions: true,
            IsDisabled: isProcessRunning);
    }

    public static bool IsMessageEligible(ChatMessage? message, SuperpowersQuickActionEligibility eligibility)
    {
        if (!eligibility.ShowQuickActions || message == null)
        {
            return false;
        }

        return string.Equals(message.Id, eligibility.MessageId, StringComparison.Ordinal)
               && string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
               && message.IsCompleted
               && !message.HasError;
    }

    private static bool SessionContainsSuperpowers(IEnumerable<ChatMessage> messages)
    {
        return messages.Any(static message =>
            !string.IsNullOrWhiteSpace(message?.Content)
            && message.Content.Contains("superpowers", StringComparison.OrdinalIgnoreCase));
    }
}

public readonly record struct SuperpowersQuickActionEligibility(
    string? MessageId,
    bool ShowQuickActions,
    bool IsDisabled)
{
    public static SuperpowersQuickActionEligibility Hidden => new(null, false, false);
}
