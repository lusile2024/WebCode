using System.Text.Json.Serialization;

namespace WebCodeCli.Domain.Domain.Model.Channels;

/// <summary>
/// 飞书帮助卡片回调动作模型
/// 用于解析卡片按钮点击时的action.value
/// </summary>
public class FeishuHelpCardAction
{
    /// <summary>
    /// 动作类型
    /// refresh_commands, select_command, back_to_list, execute_command
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 命令ID（select_command时使用）
    /// </summary>
    [JsonPropertyName("command_id")]
    public string? CommandId { get; set; }

    /// <summary>
    /// 命令内容（execute_command时使用）
    /// </summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }
}
