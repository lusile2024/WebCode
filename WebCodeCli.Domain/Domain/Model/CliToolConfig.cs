namespace WebCodeCli.Domain.Domain.Model;

/// <summary>
/// CLI 工具配置
/// </summary>
public class CliToolConfig
{
    /// <summary>
    /// 工具唯一标识
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 工具显示名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工具描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// CLI 可执行文件路径或命令
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// 参数模板，使用 {prompt} 作为用户输入的占位符
    /// </summary>
    public string ArgumentTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 少打断执行参数模板，使用 {cliThreadId} 作为原生会话占位符
    /// </summary>
    public string LowInterruptionArgumentTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 持久化模式参数（用于启动长期运行的交互式进程）
    /// 如果为null，则使用一次性进程模式
    /// </summary>
    public string? PersistentModeArguments { get; set; }

    /// <summary>
    /// 是否启用持久化进程模式（在同一会话中复用进程）
    /// </summary>
    public bool UsePersistentProcess { get; set; } = false;

    /// <summary>
    /// 工作目录
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 超时时间（秒），0 表示无限制
    /// </summary>
    public int TimeoutSeconds { get; set; } = 0;

    /// <summary>
    /// 环境变量
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }
}

