using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Common.Options;

namespace WebCodeCli.Domain.Domain.Service.Channels;

[ServiceDescription(typeof(ReplyTtsChunker), ServiceLifetime.Scoped)]
public sealed class ReplyTtsChunker
{
    private readonly int _maxChars;

    [ActivatorUtilitiesConstructor]
    public ReplyTtsChunker(IOptions<FeishuReplyTtsOptions> options)
        : this(options?.Value?.TtsChunkMaxChars ?? 1200)
    {
    }

    public ReplyTtsChunker(int maxChars)
    {
        if (maxChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChars), "Max chars must be greater than zero.");
        }

        _maxChars = maxChars;
    }

    public IReadOnlyList<string> Split(string? text)
    {
        var normalized = NormalizeInput(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > _maxChars)
            {
                FlushCurrentChunk(chunks, current);
                foreach (var paragraphChunk in SplitLongSegment(paragraph))
                {
                    chunks.Add(paragraphChunk);
                }

                continue;
            }

            if (current.Length == 0)
            {
                current.Append(paragraph);
                continue;
            }

            if (current.Length + 2 + paragraph.Length <= _maxChars)
            {
                current.Append("\n\n");
                current.Append(paragraph);
                continue;
            }

            FlushCurrentChunk(chunks, current);
            current.Append(paragraph);
        }

        FlushCurrentChunk(chunks, current);
        return chunks;
    }

    private IEnumerable<string> SplitLongSegment(string segment)
    {
        var sentenceChunks = CombineWithinLimit(SplitByDelimiters(segment, ['。', '！', '？', '!', '?', ';', '；']));
        if (sentenceChunks.Count > 1 || sentenceChunks[0].Length <= _maxChars)
        {
            return sentenceChunks;
        }

        var clauseChunks = CombineWithinLimit(SplitByDelimiters(segment, ['，', ',', '、', ':', '：']));
        if (clauseChunks.Count > 1 || clauseChunks[0].Length <= _maxChars)
        {
            return clauseChunks;
        }

        return HardBreak(segment);
    }

    private List<string> CombineWithinLimit(IReadOnlyList<string> pieces)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var piece in pieces.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            var trimmed = piece.Trim();
            if (trimmed.Length > _maxChars)
            {
                FlushCurrentChunk(chunks, current);
                chunks.AddRange(HardBreak(trimmed));
                continue;
            }

            if (current.Length == 0)
            {
                current.Append(trimmed);
                continue;
            }

            if (current.Length + 1 + trimmed.Length <= _maxChars)
            {
                current.Append(' ');
                current.Append(trimmed);
                continue;
            }

            FlushCurrentChunk(chunks, current);
            current.Append(trimmed);
        }

        FlushCurrentChunk(chunks, current);
        return chunks;
    }

    private List<string> HardBreak(string segment)
    {
        var chunks = new List<string>();
        var remaining = segment.Trim();

        while (remaining.Length > _maxChars)
        {
            var breakIndex = remaining.LastIndexOf(' ', _maxChars);
            if (breakIndex <= 0)
            {
                breakIndex = _maxChars;
            }

            chunks.Add(remaining[..breakIndex].Trim());
            remaining = remaining[breakIndex..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            chunks.Add(remaining);
        }

        return chunks;
    }

    private static List<string> SplitByDelimiters(string segment, IReadOnlyCollection<char> delimiters)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();

        foreach (var character in segment)
        {
            current.Append(character);
            if (delimiters.Contains(character))
            {
                pieces.Add(current.ToString().Trim());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            pieces.Add(current.ToString().Trim());
        }

        return pieces;
    }

    private static void FlushCurrentChunk(ICollection<string> chunks, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        chunks.Add(current.ToString().Trim());
        current.Clear();
    }

    private static string NormalizeInput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = normalized
            .Split('\n')
            .Select(static line => line.TrimEnd())
            .ToArray();

        return string.Join("\n", lines).Trim();
    }
}
