using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

internal static class FeishuStreamingTopChipLayout
{
    private const int MaxItemsPerRow = 6;

    public static IEnumerable<object> BuildModules(
        IEnumerable<FeishuStreamingCardTopChipGroup> groups,
        Func<FeishuStreamingCardTopChipItem, object> buttonFactory)
    {
        foreach (var group in groups.Where(HasVisibleContent))
        {
            if (!string.IsNullOrWhiteSpace(group.SummaryMarkdown) || group.OverflowOptions.Count > 0)
            {
                yield return BuildSummaryModule(group);
                continue;
            }

            var items = group.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .ToArray();

            if (items.Length == 0)
            {
                continue;
            }

            foreach (var rowItems in items.Chunk(MaxItemsPerRow))
            {
                var columns = rowItems
                    .Select(item => BuildColumn(buttonFactory(item)))
                    .ToArray();

                yield return new
                {
                    tag = "column_set",
                    flex_mode = "none",
                    horizontal_spacing = "4px",
                    columns
                };
            }
        }
    }

    private static bool HasVisibleContent(FeishuStreamingCardTopChipGroup group)
    {
        return !string.IsNullOrWhiteSpace(group.SummaryMarkdown)
               || group.OverflowOptions.Count > 0
               || group.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text));
    }

    public static object BuildButton(FeishuStreamingCardTopChipItem item)
    {
        var button = new Dictionary<string, object?>
        {
            ["tag"] = "button",
            ["size"] = "small",
            ["text"] = new
            {
                tag = "plain_text",
                content = item.Text
            },
            ["type"] = item.IsActive ? "primary" : "default"
        };

        if (item.IsEnabled && item.Value != null)
        {
            button["behaviors"] = new[]
            {
                new
                {
                    type = "callback",
                    value = item.Value
                }
            };
        }

        return button;
    }

    private static object BuildSummaryModule(FeishuStreamingCardTopChipGroup group)
    {
        var summaryMarkdown = string.IsNullOrWhiteSpace(group.SummaryMarkdown)
            ? "当前设置"
            : group.SummaryMarkdown;

        if (group.IsEnabled && group.OverflowOptions.Count > 0)
        {
            return new
            {
                tag = "div",
                text = new
                {
                    tag = "lark_md",
                    content = summaryMarkdown
                },
                extra = new
                {
                    tag = "overflow",
                    options = BuildOverflowOptions(group.OverflowOptions)
                }
            };
        }

        return new
        {
            tag = "div",
            text = new
            {
                tag = "lark_md",
                content = summaryMarkdown
            }
        };
    }

    private static object BuildColumn(object button)
    {
        return new
        {
            tag = "column",
            vertical_align = "top",
            elements = new[] { button }
        };
    }

    private static object[] BuildOverflowOptions(IEnumerable<FeishuStreamingCardOverflowOption> options)
    {
        return options
            .Where(option => !string.IsNullOrWhiteSpace(option.Text))
            .Select(option => (object)new
            {
                text = new
                {
                    tag = "plain_text",
                    content = option.Text
                },
                value = System.Text.Json.JsonSerializer.Serialize(option.Value)
            })
            .ToArray();
    }
}
