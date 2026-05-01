using System.Text;
using System.Text.Json;

namespace WebCodeCli.Domain.Domain.Service.Channels;

internal static class FeishuPromptNormalizer
{
    public static string Normalize(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return NormalizeCore(content, 0);
    }

    private static string NormalizeCore(string content, int depth)
    {
        if (depth > 2 || !LooksLikeJson(content))
        {
            return content;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (TryGetPostDocument(root, out var postDocument))
            {
                var rendered = RenderPostDocument(postDocument);
                return string.IsNullOrWhiteSpace(rendered) ? content : rendered;
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("content", out var nestedContent)
                && nestedContent.ValueKind == JsonValueKind.String)
            {
                var nested = nestedContent.GetString();
                if (!string.IsNullOrWhiteSpace(nested)
                    && !string.Equals(nested, content, StringComparison.Ordinal))
                {
                    return NormalizeCore(nested, depth + 1);
                }
            }
        }
        catch (JsonException)
        {
            return content;
        }

        return content;
    }

    private static bool TryGetPostDocument(JsonElement root, out JsonElement postDocument)
    {
        if (IsPostDocument(root))
        {
            postDocument = root;
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (IsPostDocument(property.Value))
                {
                    postDocument = property.Value;
                    return true;
                }
            }
        }

        postDocument = default;
        return false;
    }

    private static bool IsPostDocument(JsonElement candidate)
    {
        return candidate.ValueKind == JsonValueKind.Object
               && candidate.TryGetProperty("content", out var content)
               && content.ValueKind == JsonValueKind.Array;
    }

    private static string RenderPostDocument(JsonElement postDocument)
    {
        var builder = new StringBuilder();

        if (postDocument.TryGetProperty("title", out var titleElement)
            && titleElement.ValueKind == JsonValueKind.String)
        {
            var title = titleElement.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                builder.Append("# ").AppendLine(title);
                builder.AppendLine();
            }
        }

        if (!postDocument.TryGetProperty("content", out var contentElement)
            || contentElement.ValueKind != JsonValueKind.Array)
        {
            return builder.ToString().Trim();
        }

        var appendedLine = false;
        foreach (var block in contentElement.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var line = RenderLine(block);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (appendedLine)
            {
                builder.AppendLine();
            }

            builder.Append(line);
            appendedLine = true;
        }

        return builder.ToString().Trim();
    }

    private static string RenderLine(JsonElement block)
    {
        var builder = new StringBuilder();

        foreach (var node in block.EnumerateArray())
        {
            var text = RenderInlineNode(node);
            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.Append(text);
            }
        }

        return builder.ToString().Trim();
    }

    private static string RenderInlineNode(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var tag = node.TryGetProperty("tag", out var tagElement) && tagElement.ValueKind == JsonValueKind.String
            ? tagElement.GetString()
            : null;

        return tag switch
        {
            "text" => ReadString(node, "text"),
            "a" => RenderLink(node),
            "at" => RenderMention(node),
            "img" => "[image]",
            "media" => "[media]",
            "code_block" => ReadString(node, "text"),
            _ => ReadString(node, "text")
                 ?? ReadString(node, "content")
                 ?? ReadString(node, "name")
                 ?? string.Empty
        };
    }

    private static string RenderLink(JsonElement node)
    {
        var text = ReadString(node, "text");
        var href = ReadString(node, "href");

        if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(href))
        {
            return $"[{text}]({href})";
        }

        return text ?? href ?? string.Empty;
    }

    private static string RenderMention(JsonElement node)
    {
        var mentionText = ReadString(node, "user_name")
                          ?? ReadString(node, "name")
                          ?? ReadString(node, "text");

        if (string.IsNullOrWhiteSpace(mentionText))
        {
            return string.Empty;
        }

        return mentionText.StartsWith('@') ? mentionText : $"@{mentionText}";
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool LooksLikeJson(string content)
    {
        foreach (var ch in content)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            return ch is '{' or '[';
        }

        return false;
    }
}
