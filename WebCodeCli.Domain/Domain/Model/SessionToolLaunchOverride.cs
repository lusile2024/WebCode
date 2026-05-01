namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// 会话级工具启动覆盖项
/// </summary>
public class SessionToolLaunchOverride
{
    /// <summary>
    /// 会话级模型覆盖值
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 会话级思考等级覆盖值（仅 Codex 使用）
    /// </summary>
    public string? ReasoningEffort { get; set; }
}
