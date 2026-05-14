using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public class FeishuAttachmentDraftServiceTests
{
    [Fact]
    public void StartDraft_CreatesIsolatedDraftPerChatAndSender()
    {
        var service = new FeishuAttachmentDraftService();

        var firstDraft = service.StartDraft(
            "cli-app",
            "chat-alpha",
            "sender-a",
            new FeishuIncomingAttachment
            {
                MessageType = "image",
                AttachmentKey = "img-first",
                DisplayName = "diagram.png",
                MimeType = "image/png"
            });
        var secondDraft = service.StartDraft(
            "cli-app",
            "chat-alpha",
            "sender-b",
            new FeishuIncomingAttachment
            {
                MessageType = "file",
                AttachmentKey = "file-second",
                DisplayName = "notes.txt",
                MimeType = "text/plain"
            });

        Assert.NotSame(firstDraft, secondDraft);
        Assert.Equal("sender-a", firstDraft.SenderId);
        Assert.Equal("sender-b", secondDraft.SenderId);
        Assert.Single(firstDraft.Attachments);
        Assert.Single(secondDraft.Attachments);
        Assert.Equal("img-first", firstDraft.Attachments[0].AttachmentKey);
        Assert.Equal("file-second", secondDraft.Attachments[0].AttachmentKey);

        var retrievedFirst = service.GetDraft("cli-app", "chat-alpha", "sender-a");
        var retrievedSecond = service.GetDraft("cli-app", "chat-alpha", "sender-b");

        Assert.Same(firstDraft, retrievedFirst);
        Assert.Same(secondDraft, retrievedSecond);
    }

    [Fact]
    public void UpdateTextAndAddAttachment_PreservesDraftUntilExplicitClear()
    {
        var service = new FeishuAttachmentDraftService();
        var draft = service.StartDraft(
            "cli-app",
            "chat-alpha",
            "sender-a",
            new FeishuIncomingAttachment
            {
                MessageType = "image",
                AttachmentKey = "img-first",
                DisplayName = "diagram.png",
                MimeType = "image/png"
            });

        service.UpdateText("cli-app", "chat-alpha", "sender-a", "Review both files together.");
        service.AddAttachment(
            "cli-app",
            "chat-alpha",
            "sender-a",
            new FeishuIncomingAttachment
            {
                MessageType = "file",
                AttachmentKey = "file-second",
                DisplayName = "requirements.pdf",
                MimeType = "application/pdf"
            });

        var persisted = service.GetDraft("cli-app", "chat-alpha", "sender-a");

        Assert.Same(draft, persisted);
        Assert.NotNull(persisted);
        Assert.Equal("Review both files together.", persisted.Text);
        Assert.Equal(2, persisted.Attachments.Count);
        Assert.Equal(
            ["img-first", "file-second"],
            persisted.Attachments.Select(attachment => attachment.AttachmentKey).ToArray());

        service.ClearDraft("cli-app", "chat-alpha", "sender-a");

        Assert.Null(service.GetDraft("cli-app", "chat-alpha", "sender-a"));
    }
}
