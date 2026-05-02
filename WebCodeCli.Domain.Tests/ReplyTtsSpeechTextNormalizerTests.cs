using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public sealed class ReplyTtsSpeechTextNormalizerTests
{
    [Fact]
    public void Normalize_RemovesMarkdownLinksAndCodeBlocks()
    {
        var normalizer = new ReplyTtsSpeechTextNormalizer();

        var result = normalizer.Normalize(
            """
            # Heading

            - **Bold** item with [docs](https://example.com/docs)
            Visit https://example.com/raw for more.

            ```csharp
            Console.WriteLine("hi");
            ```
            """);

        Assert.Equal(
            """
            Heading
            Bold item with docs
            Visit this link for more.

            Code snippet omitted.
            """,
            result);
    }

    [Fact]
    public void Normalize_WhenContentBecomesEmpty_ReturnsEmptyString()
    {
        var normalizer = new ReplyTtsSpeechTextNormalizer();

        var result = normalizer.Normalize(
            """
            ```text
            only code
            ```

            https://example.com
            """);

        Assert.Equal("Code snippet omitted.\n\nthis link", result);
    }
}
