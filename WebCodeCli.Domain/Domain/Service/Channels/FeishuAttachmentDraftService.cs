using System.Collections.Concurrent;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

public class FeishuAttachmentDraftService : IFeishuAttachmentDraftService
{
    private readonly ConcurrentDictionary<string, DraftEntry> _drafts = new(StringComparer.Ordinal);

    public FeishuAttachmentDraftState StartDraft(string? appId, string chatId, string senderId, FeishuIncomingAttachment attachment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);
        ArgumentNullException.ThrowIfNull(attachment);

        var now = DateTime.UtcNow;
        var draft = new DraftEntry
        {
            AppId = NormalizeAppId(appId),
            ChatId = chatId,
            SenderId = senderId,
            Attachments = [CloneAttachment(attachment)],
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _drafts[BuildKey(appId, chatId, senderId)] = draft;
        return CreateSnapshot(draft);
    }

    public FeishuAttachmentDraftState? GetDraft(string? appId, string chatId, string senderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);

        return _drafts.TryGetValue(BuildKey(appId, chatId, senderId), out var draft)
            ? CreateSnapshot(draft)
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
            return CreateSnapshot(draft);
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
            draft.Attachments.Add(CloneAttachment(attachment));
            draft.UpdatedAtUtc = DateTime.UtcNow;
            return CreateSnapshot(draft);
        }
    }

    public void ClearDraft(string? appId, string chatId, string senderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);

        _drafts.TryRemove(BuildKey(appId, chatId, senderId), out _);
    }

    private DraftEntry GetOrCreateDraft(string? appId, string chatId, string senderId)
    {
        return _drafts.GetOrAdd(
            BuildKey(appId, chatId, senderId),
            _ =>
            {
                var now = DateTime.UtcNow;
                return new DraftEntry
                {
                    AppId = NormalizeAppId(appId),
                    ChatId = chatId,
                    SenderId = senderId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
            });
    }

    private static FeishuAttachmentDraftState CreateSnapshot(DraftEntry draft)
    {
        lock (draft)
        {
            return new FeishuAttachmentDraftState
            {
                AppId = draft.AppId,
                ChatId = draft.ChatId,
                SenderId = draft.SenderId,
                Text = draft.Text,
                Attachments = draft.Attachments.Select(CloneAttachment).ToList(),
                CreatedAtUtc = draft.CreatedAtUtc,
                UpdatedAtUtc = draft.UpdatedAtUtc
            };
        }
    }

    private static FeishuIncomingAttachment CloneAttachment(FeishuIncomingAttachment attachment)
    {
        return new FeishuIncomingAttachment
        {
            MessageType = attachment.MessageType,
            AttachmentKey = attachment.AttachmentKey,
            DisplayName = attachment.DisplayName,
            MimeType = attachment.MimeType,
            SizeBytes = attachment.SizeBytes
        };
    }

    private static string BuildKey(string? appId, string chatId, string senderId)
    {
        return string.Join("::", NormalizeAppId(appId), chatId, senderId);
    }

    private static string NormalizeAppId(string? appId)
    {
        return appId ?? string.Empty;
    }

    private sealed class DraftEntry
    {
        public string AppId { get; set; } = string.Empty;

        public string ChatId { get; set; } = string.Empty;

        public string SenderId { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public List<FeishuIncomingAttachment> Attachments { get; set; } = new();

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }
}
