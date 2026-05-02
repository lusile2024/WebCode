using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public sealed class ReplyTtsChunkerTests
{
    [Fact]
    public void Split_WhenParagraphsFitLimit_KeepsSingleChunk()
    {
        var chunker = new ReplyTtsChunker(maxChars: 80);

        var chunks = chunker.Split("第一段很短。\n\n第二段也很短。");

        Assert.Collection(
            chunks,
            chunk => Assert.Equal("第一段很短。\n\n第二段也很短。", chunk));
    }

    [Fact]
    public void Split_WhenParagraphExceedsLimit_SplitsOnSentenceBoundariesFirst()
    {
        var chunker = new ReplyTtsChunker(maxChars: 10);

        var chunks = chunker.Split("第一句很短。第二句也短。第三句也短。");

        Assert.Collection(
            chunks,
            chunk => Assert.Equal("第一句很短。", chunk),
            chunk => Assert.Equal("第二句也短。", chunk),
            chunk => Assert.Equal("第三句也短。", chunk));
    }
}
