using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

public interface IFeishuAttachmentDraftService
{
    FeishuAttachmentDraftState StartDraft(string? appId, string chatId, string senderId, FeishuIncomingAttachment attachment);

    FeishuAttachmentDraftState? GetDraft(string? appId, string chatId, string senderId);

    FeishuAttachmentDraftState UpdateText(string? appId, string chatId, string senderId, string text);

    FeishuAttachmentDraftState AddAttachment(string? appId, string chatId, string senderId, FeishuIncomingAttachment attachment);

    void ClearDraft(string? appId, string chatId, string senderId);
}
