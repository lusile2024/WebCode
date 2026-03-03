using System.Text.Json;
using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

/// <summary>
/// 飞书帮助卡片构建器
/// 构建各类帮助卡片的JSON
/// </summary>
public class FeishuHelpCardBuilder
{
    /// <summary>
    /// 构建命令选择卡片（卡片1）
    /// </summary>
    /// <param name="categories">命令分组列表</param>
    /// <param name="showRefreshButton">是否显示刷新按钮</param>
    /// <returns>飞书卡片JSON</returns>
    public string BuildCommandListCard(List<FeishuCommandCategory> categories, bool showRefreshButton = true)
    {
        var elements = new List<object>();

        // 更新按钮 - 使用 column_set + button 而非 action 标签
        if (showRefreshButton)
        {
            elements.Add(new
            {
                tag = "column_set",
                flex_mode = "none",
                background_style = "default",
                columns = new[]
                {
                    new
                    {
                        tag = "column",
                        width = "weighted",
                        weight = 1,
                        vertical_align = "top",
                        elements = new[]
                        {
                            new
                            {
                                tag = "button",
                                text = new { tag = "plain_text", content = "🔄 更新命令列表" },
                                type = "default",
                                value = JsonSerializer.Serialize(new { action = "refresh_commands" })
                            }
                        }
                    }
                }
            });
        }

        // 每个分组一个 overflow
        foreach (var category in categories)
        {
            if (category.Commands.Count == 0)
                continue;

            var options = category.Commands.Select(cmd => new
            {
                text = new { tag = "plain_text", content = cmd.Name },
                value = JsonSerializer.Serialize(new { action = "select_command", command_id = cmd.Id })
            }).ToArray();

            elements.Add(new
            {
                tag = "div",
                text = new { tag = "lark_md", content = $"**{category.Name}**" },
                extra = new
                {
                    tag = "overflow",
                    options = options
                }
            });
        }

        var card = new
        {
            schema = "2.0",
            config = new { enable_forward = true },
            header = new
            {
                template = "blue",
                title = new { tag = "plain_text", content = "🤖 Claude Code CLI 命令帮助" }
            },
            body = new { elements = elements }
        };

        return JsonSerializer.Serialize(card);
    }

    /// <summary>
    /// 构建执行卡片（卡片2）
    /// </summary>
    /// <param name="command">选中的命令</param>
    /// <returns>飞书卡片JSON</returns>
    public string BuildExecuteCard(FeishuCommand command)
    {
        var elements = new List<object>
        {
            new
            {
                tag = "div",
                text = new { tag = "lark_md", content = $"**📝 命令说明：**\n{command.Description}" }
            },
            new
            {
                tag = "div",
                text = new { tag = "lark_md", content = $"**💡 用法示例：**\n`{command.Usage}`" }
            },
            new
            {
                tag = "hr"
            },
            new
            {
                tag = "input",
                name = "command_input",
                placeholder = "编辑命令...",
                default_value = command.Name.StartsWith("/") ? command.Name : $"claude {command.Name}"
            },
            new
            {
                tag = "column_set",
                flex_mode = "none",
                background_style = "default",
                columns = new[]
                {
                    new
                    {
                        tag = "column",
                        width = "weighted",
                        weight = 1,
                        vertical_align = "top",
                        elements = new[]
                        {
                            new
                            {
                                tag = "button",
                                text = new { tag = "plain_text", content = "❌ 取消" },
                                type = "default",
                                action_type = "request",
                                value = JsonSerializer.Serialize(new { action = "back_to_list" })
                            }
                        }
                    },
                    new
                    {
                        tag = "column",
                        width = "weighted",
                        weight = 1,
                        vertical_align = "top",
                        elements = new[]
                        {
                            new
                            {
                                tag = "button",
                                text = new { tag = "plain_text", content = "✅ 执行命令" },
                                type = "primary",
                                action_type = "form_submit",
                                value = JsonSerializer.Serialize(new { action = "execute_command" })
                            }
                        }
                    }
                }
            }
        };

        var card = new
        {
            schema = "2.0",
            config = new { enable_forward = true },
            header = new
            {
                template = "purple",
                title = new { tag = "plain_text", content = $"⚙️ 执行命令：{command.Name}" }
            },
            body = new { elements = elements }
        };

        return JsonSerializer.Serialize(card);
    }

    /// <summary>
    /// 构建过滤后的搜索卡片
    /// </summary>
    /// <param name="categories">过滤后的命令分组</param>
    /// <param name="keyword">搜索关键词</param>
    /// <returns>飞书卡片JSON</returns>
    public string BuildFilteredCard(List<FeishuCommandCategory> categories, string keyword)
    {
        var elements = new List<object>();

        // 过滤说明
        elements.Add(new
        {
            tag = "div",
            text = new { tag = "lark_md", content = $"🔍 搜索结果：**{keyword}**\n\n点击下方按钮返回完整列表" }
        });

        // 返回按钮 - 使用 column_set + button 而非 action 标签
        elements.Add(new
        {
            tag = "column_set",
            flex_mode = "none",
            background_style = "default",
            columns = new[]
            {
                new
                {
                    tag = "column",
                    width = "weighted",
                    weight = 1,
                    vertical_align = "top",
                    elements = new[]
                    {
                        new
                        {
                            tag = "button",
                            text = new { tag = "plain_text", content = "📋 显示全部命令" },
                            type = "default",
                            value = JsonSerializer.Serialize(new { action = "back_to_list" })
                        }
                    }
                }
            }
        });

        elements.Add(new { tag = "hr" });

        // 匹配的命令（不分组，直接显示）
        var allCommands = categories.SelectMany(c => c.Commands).ToList();
        if (allCommands.Count > 0)
        {
            var options = allCommands.Select(cmd => new
            {
                text = new { tag = "plain_text", content = $"{cmd.Name} - {cmd.Description}" },
                value = JsonSerializer.Serialize(new { action = "select_command", command_id = cmd.Id })
            }).ToArray();

            elements.Add(new
            {
                tag = "div",
                text = new { tag = "lark_md", content = $"**找到 {allCommands.Count} 个匹配命令：**" },
                extra = new
                {
                    tag = "overflow",
                    options = options
                }
            });
        }
        else
        {
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "lark_md", content = "❌ 未找到匹配的命令，请尝试其他关键词" }
            });
        }

        var card = new
        {
            schema = "2.0",
            config = new { enable_forward = true },
            header = new
            {
                template = "turquoise",
                title = new { tag = "plain_text", content = "🔍 命令搜索" }
            },
            body = new { elements = elements }
        };

        return JsonSerializer.Serialize(card);
    }

    /// <summary>
    /// 构建带toast的卡片响应
    /// </summary>
    /// <param name="cardJson">卡片JSON</param>
    /// <param name="toastMessage">toast消息</param>
    /// <param name="toastType">toast类型</param>
    /// <returns>飞书响应对象</returns>
    public object BuildToastResponse(string cardJson, string toastMessage, string toastType = "info")
    {
        return new
        {
            toast = new
            {
                type = toastType,
                content = toastMessage
            },
            card = new
            {
                type = "raw",
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(cardJson)
            }
        };
    }

    /// <summary>
    /// 构建仅toast的响应
    /// </summary>
    /// <param name="toastMessage">toast消息</param>
    /// <param name="toastType">toast类型</param>
    /// <returns>飞书响应对象</returns>
    public object BuildToastOnlyResponse(string toastMessage, string toastType = "info")
    {
        return new
        {
            toast = new
            {
                type = toastType,
                content = toastMessage
            }
        };
    }
}
