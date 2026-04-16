namespace WebCodeCli.Domain.Domain.Model.Channels;

/// <summary>
/// 飞书流式回复句柄
/// </summary>
public class FeishuStreamingHandle : IDisposable
{
    private readonly Func<string, int, Task> _updateAsync;
    private readonly Func<string, int, Task> _finishAsync;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly int _throttleMs;
    private DateTime _lastUpdate = DateTime.MinValue;
    private int _sequence = 0;
    private bool _disposed = false;
    private bool _isFinished = false;

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
        Func<string, int, Task> updateAsync,
        Func<string, int, Task> finishAsync,
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
        if (_disposed || _isFinished)
        {
            return;
        }

        await _operationLock.WaitAsync();
        try
        {
            if (_disposed || _isFinished)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastUpdate).TotalMilliseconds < _throttleMs)
            {
                return; // 节流跳过
            }

            _lastUpdate = now;
            var sequence = Interlocked.Increment(ref _sequence);
            await _updateAsync(content, sequence);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// 完成更新
    /// </summary>
    public async Task FinishAsync(string finalContent)
    {
        if (_disposed)
        {
            return;
        }

        await _operationLock.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _isFinished = true;
            var sequence = Interlocked.Increment(ref _sequence);
            await _finishAsync(finalContent, sequence);
            _disposed = true;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _isFinished = true;
    }
}
