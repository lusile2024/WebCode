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

    /// <summary>
    /// 会话ID（会话管理时使用）
    /// </summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    /// <summary>
    /// 聊天Key（会话管理时使用，格式：feishu:{AppId}:{ChatId}）
    /// </summary>
    [JsonPropertyName("chat_key")]
    public string? ChatKey { get; set; }

    /// <summary>
    /// 创建会话模式（default/custom/existing）
    /// </summary>
    [JsonPropertyName("create_mode")]
    public string? CreateMode { get; set; }

    /// <summary>
    /// 工作区路径（已有目录选择时使用）
    /// </summary>
    [JsonPropertyName("workspace_path")]
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// CLI 工具 ID（新建会话选择工具时使用）
    /// </summary>
    [JsonPropertyName("tool_id")]
    public string? ToolId { get; set; }
}
