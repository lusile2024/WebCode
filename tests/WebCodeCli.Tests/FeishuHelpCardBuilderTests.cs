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
        var categoryRow = elements.EnumerateArray().Last();
        var button = categoryRow
            .GetProperty("columns")[1]
            .GetProperty("elements")[0];
        var callbackValue = button
            .GetProperty("behaviors")[0]
            .GetProperty("value");

        Assert.False(categoryRow.TryGetProperty("extra", out _));
        Assert.Equal("show_category", callbackValue.GetProperty("action").GetString());
        Assert.Equal("general", callbackValue.GetProperty("category_id").GetString());
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
        var resultBlock = elements.EnumerateArray().Last();
        var button = resultBlock
            .GetProperty("columns")[1]
            .GetProperty("elements")[0];
        var callbackValue = button
            .GetProperty("behaviors")[0]
            .GetProperty("value");

        Assert.False(resultBlock.TryGetProperty("extra", out _));
        Assert.Equal("select_command", callbackValue.GetProperty("action").GetString());
        Assert.Equal("init", callbackValue.GetProperty("command_id").GetString());
    }
}
