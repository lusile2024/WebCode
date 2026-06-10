using System.Text.RegularExpressions;

namespace WebCodeCli.Domain.Domain.Service.Channels;

public static partial class MarkdownReferenceExtractor
{
    private static readonly Regex BareMarkdownPathRegex = new(
        @"(?<path>(?:[A-Za-z]:[\\/]|\.{1,2}[\\/]|[A-Za-z0-9_\-]+(?:[./\\][A-Za-z0-9_\-]+)*[\\/])[^\\/:*?""<>|\r\n]+\.md)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MarkdownLinkRegex = new(
        @"\[[^\]]+\]\((?<path>[^)\r\n]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<ReferencedMarkdownDocumentCandidate> Extract(string? text, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return [];
        }

        if (!Directory.Exists(workspaceRoot))
        {
            return [];
        }

        var normalizedWorkspaceRoot = Path.GetFullPath(workspaceRoot.Trim());
        var results = new List<ReferencedMarkdownDocumentCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in EnumerateCandidatePaths(text))
        {
            var candidate = TryResolveCandidate(rawPath, normalizedWorkspaceRoot);
            if (candidate == null)
            {
                continue;
            }

            if (seen.Add(candidate.RelativePath))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string text)
    {
        var matches = new List<(int Index, int Length, string Path)>();

        foreach (Match match in MarkdownLinkRegex.Matches(text))
        {
            if (!match.Groups["path"].Success)
            {
                continue;
            }

            var path = match.Groups["path"].Value;
            if (IsSupportedMarkdownLinkTarget(path))
            {
                matches.Add((match.Index, match.Length, path));
            }
        }

        foreach (Match match in BareMarkdownPathRegex.Matches(text))
        {
            if (!match.Groups["path"].Success)
            {
                continue;
            }

            if (IsRemoteUrlContext(text, match.Index, match.Length)
                || IsFollowedByQueryOrAnchor(text, match.Index + match.Length))
            {
                continue;
            }

            var path = match.Groups["path"].Value;
            if (!string.IsNullOrWhiteSpace(path))
            {
                matches.Add((match.Index, match.Length, path));
            }
        }

        foreach (var match in matches.OrderBy(static item => item.Index).ThenBy(static item => item.Length))
        {
            yield return match.Path;
        }
    }

    private static bool IsSupportedMarkdownLinkTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim().Trim('"', '\'', '`', '<', '>', '(', ')', '{', '}', '[', ']');
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemoteUrlContext(string text, int matchIndex, int matchLength)
    {
        var tokenStart = matchIndex;
        while (tokenStart > 0 && !IsTokenBoundary(text[tokenStart - 1]))
        {
            tokenStart--;
        }

        var tokenEnd = matchIndex + matchLength;
        while (tokenEnd < text.Length && !IsTokenBoundary(text[tokenEnd]))
        {
            tokenEnd++;
        }

        var token = text[tokenStart..tokenEnd];
        return token.Contains("://", StringComparison.Ordinal)
               || token.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool IsTokenBoundary(char value)
    {
        return char.IsWhiteSpace(value)
               || value is '(' or ')' or '[' or ']' or '<' or '>' or '"' or '\'';
    }

    private static bool IsFollowedByQueryOrAnchor(string text, int index)
    {
        return index < text.Length && text[index] is '?' or '#';
    }

    private static ReferencedMarkdownDocumentCandidate? TryResolveCandidate(string rawPath, string workspaceRoot)
    {
        var trimmed = rawPath.Trim().Trim('"', '\'', '`', '[', ']', '(', ')', '{', '}', '<', '>', ',', ';');
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var absolutePath = Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(workspaceRoot, trimmed));

        if (!absolutePath.StartsWith(
                workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(absolutePath, workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var relativePath = Path.GetRelativePath(workspaceRoot, absolutePath).Replace('\\', '/');
        while (relativePath.StartsWith("./", StringComparison.Ordinal))
        {
            relativePath = relativePath[2..];
        }

        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("../", StringComparison.Ordinal))
        {
            return null;
        }

        return new ReferencedMarkdownDocumentCandidate(
            AbsolutePath: absolutePath,
            RelativePath: relativePath,
            Title: relativePath);
    }
}

public sealed record ReferencedMarkdownDocumentCandidate(
    string AbsolutePath,
    string RelativePath,
    string Title);
