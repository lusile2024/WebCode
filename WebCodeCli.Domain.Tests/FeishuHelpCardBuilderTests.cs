using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public class FeishuHelpCardBuilderTests
{
    private readonly FeishuHelpCardBuilder _builder = new();

    [Fact]
    public void BuildCommandListCard_UsesCategoryButtonCallback()
    {
        var cardJson = _builder.BuildCommandListCard(CreateCategories());
        var actionValue = GetActionValue(JsonDocument.Parse(cardJson).RootElement.GetProperty("body").GetProperty("elements"), "show_category");

        Assert.Equal(JsonValueKind.Object, actionValue.ValueKind);
        Assert.Equal("show_category", actionValue.GetProperty("action").GetString());
        Assert.Equal("general", actionValue.GetProperty("category_id").GetString());
    }

    [Fact]
    public void BuildFilteredCard_UsesCommandButtonCallback()
    {
        var cardJson = _builder.BuildFilteredCard(CreateCategories(), "help");
        var actionValue = GetActionValue(JsonDocument.Parse(cardJson).RootElement.GetProperty("body").GetProperty("elements"), "select_command");

        Assert.Equal(JsonValueKind.Object, actionValue.ValueKind);
        Assert.Equal("select_command", actionValue.GetProperty("action").GetString());
        Assert.Equal("feishuhelp", actionValue.GetProperty("command_id").GetString());
    }

    [Fact]
    public void BuildCommandListCardV2_UsesCategoryButtonCallback()
    {
        var card = _builder.BuildCommandListCardV2(CreateCategories(), showRefreshButton: false);
        using var bodyDoc = JsonDocument.Parse(JsonSerializer.Serialize(card.Body!.Elements));
        var actionValue = GetActionValue(bodyDoc.RootElement, "show_category");

        Assert.Equal(JsonValueKind.Object, actionValue.ValueKind);
        Assert.Equal("show_category", actionValue.GetProperty("action").GetString());
        Assert.Equal("general", actionValue.GetProperty("category_id").GetString());
        Assert.False(ContainsProperty(bodyDoc.RootElement, "overflow"));
        Assert.False(ContainsProperty(bodyDoc.RootElement, "extra"));
    }

    [Fact]
    public void BuildCategoryCommandsCardV2_UsesCommandButtonCallback()
    {
        var category = CreateCategories()[0];
        var card = _builder.BuildCategoryCommandsCardV2(category);
        using var bodyDoc = JsonDocument.Parse(JsonSerializer.Serialize(card.Body!.Elements));
        var actionValue = GetActionValue(bodyDoc.RootElement, "select_command");

        Assert.Equal(JsonValueKind.Object, actionValue.ValueKind);
        Assert.Equal("select_command", actionValue.GetProperty("action").GetString());
        Assert.Equal("feishuhelp", actionValue.GetProperty("command_id").GetString());
    }

    [Fact]
    public void BuildFilteredCardV2_UsesCommandButtonCallback()
    {
        var card = _builder.BuildFilteredCardV2(CreateCategories(), "help");
        using var bodyDoc = JsonDocument.Parse(JsonSerializer.Serialize(card.Body!.Elements));
        var actionValue = GetActionValue(bodyDoc.RootElement, "select_command");

        Assert.Equal(JsonValueKind.Object, actionValue.ValueKind);
        Assert.Equal("select_command", actionValue.GetProperty("action").GetString());
        Assert.Equal("feishuhelp", actionValue.GetProperty("command_id").GetString());
        Assert.False(ContainsProperty(bodyDoc.RootElement, "overflow"));
        Assert.False(ContainsProperty(bodyDoc.RootElement, "extra"));
    }

    private static JsonElement GetActionValue(JsonElement elements, string action)
    {
        if (TryGetActionValue(elements, action, out var actionValue))
        {
            return actionValue;
        }

        throw new Xunit.Sdk.XunitException($"No button callback found for action '{action}' in card payload.");
    }

    private static bool TryGetActionValue(JsonElement element, string action, out JsonElement actionValue)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("tag", out var tag) &&
                tag.GetString() == "button" &&
                element.TryGetProperty("behaviors", out var behaviors))
            {
                foreach (var behavior in behaviors.EnumerateArray())
                {
                    if (!behavior.TryGetProperty("value", out var value))
                    {
                        continue;
                    }

                    if (TryMatchActionValue(value, action, out actionValue))
                    {
                        return true;
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryGetActionValue(property.Value, action, out actionValue))
                {
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryGetActionValue(item, action, out actionValue))
                {
                    return true;
                }
            }
        }

        actionValue = default;
        return false;
    }

    private static bool TryMatchActionValue(JsonElement value, string action, out JsonElement actionValue)
    {
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("action", out var actionProp) &&
            actionProp.GetString() == action)
        {
            actionValue = value;
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var doc = JsonDocument.Parse(value.GetString()!);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("action", out var nestedActionProp) &&
                    nestedActionProp.GetString() == action)
                {
                    actionValue = doc.RootElement.Clone();
                    return true;
                }
            }
            catch (JsonException)
            {
            }
        }

        actionValue = default;
        return false;
    }

    private static bool ContainsProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) || ContainsProperty(property.Value, propertyName))
                {
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsProperty(item, propertyName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<FeishuCommandCategory> CreateCategories()
    {
        return
        [
            new FeishuCommandCategory
            {
                Id = "general",
                Name = "General",
                Commands =
                [
                    new FeishuCommand
                    {
                        Id = "feishuhelp",
                        Name = "/feishuhelp",
                        Description = "Show help for Feishu commands",
                        Usage = "/feishuhelp",
                        ExecuteText = "/feishuhelp",
                        Category = "General",
                        ToolId = "claude-code"
                    }
                ]
            }
        ];
    }
}
