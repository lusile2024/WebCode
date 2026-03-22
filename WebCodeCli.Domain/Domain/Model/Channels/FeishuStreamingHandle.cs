namespace WebCodeCli.Domain.Domain.Model.Channels;

/// <summary>
/// 飞书流式回复句柄
/// </summary>
public class FeishuStreamingHandle : IDisposable
{
    private readonly Func<string, Task> _updateAsync;
    private readonly Func<string, Task> _finishAsync;
    private readonly int _throttleMs;
    private DateTime _lastUpdate = DateTime.MinValue;
    private int _sequence = 0;
    private bool _disposed = false;

    /// <summary>
    /// 卡片 ID
    /// </summary>
    public string CardId { get; }

    /// <summary>
    /// 消息 ID
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    /// 当前序列号
    /// </summary>
    public int Sequence => _sequence;

    public FeishuStreamingHandle(
        string cardId,
        string messageId,
        Func<string, Task> updateAsync,
        Func<string, Task> finishAsync,
        int throttleMs = 500)
    {
        CardId = cardId;
        MessageId = messageId;
        _updateAsync = updateAsync;
        _finishAsync = finishAsync;
        _throttleMs = throttleMs;
    }

    /// <summary>
    /// 更新卡片内容（带节流）
    /// </summary>
    public async Task UpdateAsync(string content)
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        if ((now - _lastUpdate).TotalMilliseconds < _throttleMs)
            return; // 节流跳过

        _lastUpdate = now;
        _sequence++;
        await _updateAsync(content);
    }

    /// <summary>
    /// 完成更新
    /// </summary>
    public async Task FinishAsync(string finalContent)
    {
        if (_disposed) return;

        _sequence++;
        await _finishAsync(finalContent);
        _disposed = true;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
