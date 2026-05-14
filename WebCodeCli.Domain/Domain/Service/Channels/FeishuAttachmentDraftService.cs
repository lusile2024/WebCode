using System.Collections.Concurrent;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

public class FeishuAttachmentDraftService : IFeishuAttachmentDraftService
{
    private readonly ConcurrentDictionary<string, FeishuAttachmentDraftState> _drafts = new(StringComparer.Ordinal);

    public FeishuAttachmentDraftState StartDraft(string? appId, string chatId, string senderId, FeishuIncomingAttachment attachment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);
        ArgumentNullException.ThrowIfNull(attachment);

        var now = DateTime.UtcNow;
        var draft = new FeishuAttachmentDraftState
        {
            AppId = NormalizeAppId(appId),
            ChatId = chatId,
            SenderId = senderId,
            Attachments = [attachment],
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _drafts[BuildKey(appId, chatId, senderId)] = draft;
        return draft;
    }

    public FeishuAttachmentDraftState? GetDraft(string? appId, string chatId, string senderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);

        return _drafts.TryGetValue(BuildKey(appId, chatId, senderId), out var draft)
            ? draft
            : null;
    }

    public FeishuAttachmentDraftState UpdateText(string? appId, string chatId, string senderId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);

        var draft = GetOrCreateDraft(appId, chatId, senderId);
        lock (draft)
        {
            draft.Text = text ?? string.Empty;
            draft.UpdatedAtUtc = DateTime.UtcNow;
            return draft;
        }
    }

    public FeishuAttachmentDraftState AddAttachment(string? appId, string chatId, string senderId, FeishuIncomingAttachment attachment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);
        ArgumentNullException.ThrowIfNull(attachment);

        var draft = GetOrCreateDraft(appId, chatId, senderId);
        lock (draft)
        {
            draft.Attachments.Add(attachment);
            draft.UpdatedAtUtc = DateTime.UtcNow;
            return draft;
        }
    }

    public void ClearDraft(string? appId, string chatId, string senderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);

        _drafts.TryRemove(BuildKey(appId, chatId, senderId), out _);
    }

    private FeishuAttachmentDraftState GetOrCreateDraft(string? appId, string chatId, string senderId)
    {
        return _drafts.GetOrAdd(
            BuildKey(appId, chatId, senderId),
            _ =>
            {
                var now = DateTime.UtcNow;
                return new FeishuAttachmentDraftState
                {
                    AppId = NormalizeAppId(appId),
                    ChatId = chatId,
                    SenderId = senderId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
            });
    }

    private static string BuildKey(string? appId, string chatId, string senderId)
    {
        return string.Join("::", NormalizeAppId(appId), chatId, senderId);
    }

    private static string NormalizeAppId(string? appId)
    {
        return appId ?? string.Empty;
    }
}
