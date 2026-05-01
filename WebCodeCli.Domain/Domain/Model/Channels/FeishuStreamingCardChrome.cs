namespace WebCodeCli.Domain.Domain.Model.Channels;

/// <summary>
/// 流式卡片顶部附加信息
/// </summary>
public sealed class FeishuStreamingCardChrome
{
    /// <summary>
    /// 顶部状态栏 Markdown
    /// </summary>
    public string? StatusMarkdown { get; set; }

    /// <summary>
    /// 顶部右侧下拉菜单选项
    /// </summary>
    public List<FeishuStreamingCardOverflowOption> OverflowOptions { get; set; } = [];
}

internal static class FeishuStreamingStatusFormatter
{
    private static readonly string[] RunningFrames = ["／", "＼"];

    public static string WithRunningState(string baseStatusMarkdown, int frameIndex)
        => WithState(baseStatusMarkdown, $"处理中 {RunningFrames[Math.Abs(frameIndex) % RunningFrames.Length]}");

    public static string WithCompletedState(string baseStatusMarkdown)
        => WithState(baseStatusMarkdown, "已完成");

    public static string WithStoppedState(string baseStatusMarkdown)
        => WithState(baseStatusMarkdown, "已停止");

    public static string WithErrorState(string baseStatusMarkdown)
        => WithState(baseStatusMarkdown, "执行出错");

    private static string WithState(string baseStatusMarkdown, string state)
        => string.IsNullOrWhiteSpace(baseStatusMarkdown)
            ? state
            : $"{baseStatusMarkdown} · {state}";
}

/// <summary>
/// 流式卡片下拉菜单项
/// </summary>
public sealed class FeishuStreamingCardOverflowOption
{
    /// <summary>
    /// 菜单文本
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 选中后回传的动作值
    /// </summary>
    public object Value { get; set; } = new { };
}
