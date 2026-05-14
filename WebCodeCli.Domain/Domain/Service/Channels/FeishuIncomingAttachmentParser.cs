using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

public class FeishuIncomingAttachmentParser
{
    public IReadOnlyList<FeishuIncomingAttachment> Parse(string? messageType, string? rawContent)
    {
        if (string.IsNullOrWhiteSpace(messageType) || string.IsNullOrWhiteSpace(rawContent))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(rawContent);
            var root = document.RootElement;
            return messageType switch
            {
                "image" => ParseImage(root),
                "file" => ParseFile(root),
                _ => []
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<FeishuIncomingAttachment> ParseImage(JsonElement root)
    {
        var attachmentKey = GetString(root, "image_key");
        if (string.IsNullOrWhiteSpace(attachmentKey))
        {
            return [];
        }

        var displayName = GetString(root, "file_name");
        var mimeType = GetString(root, "mime_type");
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            mimeType = GuessMimeType(displayName, "image");
        }

        return
        [
            new FeishuIncomingAttachment
            {
                MessageType = "image",
                AttachmentKey = attachmentKey,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? attachmentKey : displayName,
                MimeType = mimeType,
                SizeBytes = GetInt64(root, "file_size")
            }
        ];
    }

    private static IReadOnlyList<FeishuIncomingAttachment> ParseFile(JsonElement root)
    {
        var attachmentKey = GetString(root, "file_key");
        if (string.IsNullOrWhiteSpace(attachmentKey))
        {
            return [];
        }

        var displayName = GetString(root, "file_name");
        var mimeType = GetString(root, "mime_type");
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            mimeType = GuessMimeType(displayName, "file");
        }

        return
        [
            new FeishuIncomingAttachment
            {
                MessageType = "file",
                AttachmentKey = attachmentKey,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? attachmentKey : displayName,
                MimeType = mimeType,
                SizeBytes = GetInt64(root, "file_size")
            }
        ];
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string GuessMimeType(string fileName, string fallbackCategory)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ when string.Equals(fallbackCategory, "image", StringComparison.OrdinalIgnoreCase) => "image/png",
            _ => "application/octet-stream"
        };
    }
}
