using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Channels;
using Xunit;

namespace WebCodeCli.Tests;

public sealed class FeishuHelpCardBuilderTests
{
    [Fact]
    public void BuildCommandListCard_UsesCategoryButtonsInsteadOfOverflowOptions()
    {
        var builder = new FeishuHelpCardBuilder();
        var categories = new List<FeishuCommandCategory>
        {
            new()
            {
                Id = "general",
                Name = "General",
                Commands =
                [
                    new FeishuCommand
                    {
                        Id = "init",
                        Name = "init",
                        Description = "Initialize project",
                        Usage = "init"
                    }
                ]
            }
        };

        var cardJson = builder.BuildCommandListCard(categories);
        using var document = JsonDocument.Parse(cardJson);
        var elements = document.RootElement.GetProperty("body").GetProperty("elements");
        JsonElement? callbackValue = null;
        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("columns", out var columns))
            {
                continue;
            }

            foreach (var column in columns.EnumerateArray())
            {
                if (!column.TryGetProperty("elements", out var columnElements))
                {
                    continue;
                }

                foreach (var button in columnElements.EnumerateArray())
                {
                    if (!button.TryGetProperty("behaviors", out var behaviors) || behaviors.GetArrayLength() == 0)
                    {
                        continue;
                    }

                    var value = behaviors[0].GetProperty("value");
                    if (string.Equals(value.GetProperty("action").GetString(), "show_category", StringComparison.Ordinal))
                    {
                        callbackValue = value;
                        break;
                    }
                }

                if (callbackValue is not null)
                {
                    break;
                }
            }

            if (callbackValue is not null)
            {
                break;
            }
        }

        Assert.True(callbackValue.HasValue);
        Assert.Equal("show_category", callbackValue.Value.GetProperty("action").GetString());
        Assert.Equal("general", callbackValue.Value.GetProperty("category_id").GetString());
    }

    [Fact]
    public void BuildFilteredCard_UsesCommandButtonsForSelection()
    {
        var builder = new FeishuHelpCardBuilder();
        var categories = new List<FeishuCommandCategory>
        {
            new()
            {
                Id = "general",
                Name = "General",
                Commands =
                [
                    new FeishuCommand
                    {
                        Id = "init",
                        Name = "init",
                        Description = "Initialize project",
                        Usage = "init"
                    }
                ]
            }
        };

        var cardJson = builder.BuildFilteredCard(categories, "ini");
        using var document = JsonDocument.Parse(cardJson);
        var elements = document.RootElement.GetProperty("body").GetProperty("elements");
        JsonElement? callbackValue = null;
        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("columns", out var columns))
            {
                continue;
            }

            foreach (var column in columns.EnumerateArray())
            {
                if (!column.TryGetProperty("elements", out var columnElements))
                {
                    continue;
                }

                foreach (var button in columnElements.EnumerateArray())
                {
                    if (!button.TryGetProperty("behaviors", out var behaviors) || behaviors.GetArrayLength() == 0)
                    {
                        continue;
                    }

                    var value = behaviors[0].GetProperty("value");
                    if (string.Equals(value.GetProperty("action").GetString(), "select_command", StringComparison.Ordinal))
                    {
                        callbackValue = value;
                        break;
                    }
                }

                if (callbackValue is not null)
                {
                    break;
                }
            }

            if (callbackValue is not null)
            {
                break;
            }
        }

        Assert.True(callbackValue.HasValue);
        Assert.Equal("select_command", callbackValue.Value.GetProperty("action").GetString());
        Assert.Equal("init", callbackValue.Value.GetProperty("command_id").GetString());
    }
}
