using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public class FeishuIncomingAttachmentParserTests
{
    [Fact]
    public void Parse_ImagePayload_ReturnsStructuredAttachment()
    {
        var parser = new FeishuIncomingAttachmentParser();

        var attachments = parser.Parse(
            "image",
            """
            {
              "image_key": "img_v3_02f2d1d1-structured",
              "file_name": "design-review.png"
            }
            """);

        var attachment = Assert.Single(attachments);
        Assert.Equal("image", attachment.MessageType);
        Assert.Equal("img_v3_02f2d1d1-structured", attachment.AttachmentKey);
        Assert.Equal("design-review.png", attachment.DisplayName);
        Assert.Equal("image/png", attachment.MimeType);
    }
}
