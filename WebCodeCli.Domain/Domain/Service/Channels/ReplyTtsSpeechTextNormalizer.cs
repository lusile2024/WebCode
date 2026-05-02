using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using WebCodeCli.Domain.Common.Extensions;

namespace WebCodeCli.Domain.Domain.Service.Channels;

[ServiceDescription(typeof(ReplyTtsSpeechTextNormalizer), ServiceLifetime.Singleton)]
public sealed class ReplyTtsSpeechTextNormalizer
{
    private static readonly Regex CodeBlockRegex = new("```.*?```", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRegex = new(@"\[(?<text>[^\]]+)\]\((?<url>https?://[^)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RawUrlRegex = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HeadingRegex = new(@"^\s{0,3}#{1,6}\s*", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex QuotePrefixRegex = new(@"^\s{0,3}>\s?", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BulletPrefixRegex = new(@"^\s*(?:[-+*]|\d+\.)\s+", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex SingleAsteriskRegex = new(@"(?<!\*)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex SingleUnderscoreRegex = new(@"(?<!_)_(?!_)", RegexOptions.Compiled);
    private static readonly Regex TrailingWhitespaceRegex = new(@"[ \t]+\n", RegexOptions.Compiled);
    private static readonly Regex RepeatedBlankLinesRegex = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex RepeatedSpacesRegex = new(@"[ \t]{2,}", RegexOptions.Compiled);

    public string Normalize(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var normalized = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        normalized = CodeBlockRegex.Replace(normalized, "\nCode snippet omitted.\n");
        normalized = MarkdownLinkRegex.Replace(normalized, static match => match.Groups["text"].Value.Trim());
        normalized = RawUrlRegex.Replace(normalized, "this link");
        normalized = HeadingRegex.Replace(normalized, string.Empty);
        normalized = QuotePrefixRegex.Replace(normalized, string.Empty);
        normalized = BulletPrefixRegex.Replace(normalized, string.Empty);
        normalized = normalized
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace('`', ' ');
        normalized = SingleAsteriskRegex.Replace(normalized, string.Empty);
        normalized = SingleUnderscoreRegex.Replace(normalized, string.Empty);
        normalized = TrailingWhitespaceRegex.Replace(normalized, "\n");
        normalized = RepeatedBlankLinesRegex.Replace(normalized, "\n\n");
        normalized = RepeatedSpacesRegex.Replace(normalized, " ");

        return normalized.Trim();
    }
}
