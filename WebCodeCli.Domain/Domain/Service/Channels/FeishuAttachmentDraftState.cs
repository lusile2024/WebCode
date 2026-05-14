using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

public class FeishuAttachmentDraftState
{
    public string AppId { get; set; } = string.Empty;

    public string ChatId { get; set; } = string.Empty;

    public string SenderId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public List<FeishuIncomingAttachment> Attachments { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
