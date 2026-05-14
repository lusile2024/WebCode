using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public class FeishuAttachmentDraftServiceTests
{
    [Fact]
    public void StartDraft_CreatesIsolatedDraftPerChatAndSender()
    {
        var service = new FeishuAttachmentDraftService();
        var firstAttachment = new FeishuIncomingAttachment
        {
            MessageType = "image",
            AttachmentKey = "img-first",
            DisplayName = "diagram.png",
            MimeType = "image/png"
        };

        var firstDraft = service.StartDraft(
            "cli-app",
            "chat-alpha",
            "sender-a",
            firstAttachment);
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

        firstAttachment.AttachmentKey = "mutated-after-start";
        firstDraft.Attachments[0].AttachmentKey = "mutated-return-value";

        Assert.NotSame(firstDraft, secondDraft);
        Assert.Equal("sender-a", firstDraft.SenderId);
        Assert.Equal("sender-b", secondDraft.SenderId);
        Assert.Single(firstDraft.Attachments);
        Assert.Single(secondDraft.Attachments);
        Assert.Equal("mutated-return-value", firstDraft.Attachments[0].AttachmentKey);
        Assert.Equal("file-second", secondDraft.Attachments[0].AttachmentKey);

        var retrievedFirst = service.GetDraft("cli-app", "chat-alpha", "sender-a");
        var retrievedSecond = service.GetDraft("cli-app", "chat-alpha", "sender-b");

        Assert.NotSame(firstDraft, retrievedFirst);
        Assert.NotSame(secondDraft, retrievedSecond);
        Assert.NotSame(firstDraft.Attachments[0], retrievedFirst!.Attachments[0]);
        Assert.Equal("img-first", retrievedFirst.Attachments[0].AttachmentKey);
        Assert.Equal("file-second", retrievedSecond!.Attachments[0].AttachmentKey);
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

        Assert.NotNull(persisted);
        Assert.Equal("Review both files together.", persisted.Text);
        Assert.Equal(2, persisted.Attachments.Count);
        Assert.Equal(
            ["img-first", "file-second"],
            persisted.Attachments.Select(attachment => attachment.AttachmentKey).ToArray());

        persisted.Text = "mutated snapshot";
        persisted.Attachments.Clear();
        draft.Text = "mutated original return";

        var refreshed = service.GetDraft("cli-app", "chat-alpha", "sender-a");

        Assert.NotSame(draft, refreshed);
        Assert.Equal("Review both files together.", refreshed!.Text);
        Assert.Equal(
            ["img-first", "file-second"],
            refreshed.Attachments.Select(attachment => attachment.AttachmentKey).ToArray());

        service.ClearDraft("cli-app", "chat-alpha", "sender-a");

        Assert.Null(service.GetDraft("cli-app", "chat-alpha", "sender-a"));
    }
}
