using WebCodeCli.Domain.Domain.Model.Channels;

namespace WebCodeCli.Domain.Domain.Service.Channels;

internal sealed class FeishuStreamingCardSession
{
    internal const int MaxReplacementCardsPerLogicalStream = 10;

    private readonly Func<FeishuStreamingHandle, string, CancellationToken, Task<FeishuStreamingHandle?>> _replacementFactory;
    private readonly Action<FeishuStreamingHandle> _handleChanged;
    private readonly Func<FeishuStreamingHandle, string, CancellationToken, Task>? _stoppedHandleFinalizer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _replacementCount;

    public FeishuStreamingCardSession(
        FeishuStreamingHandle initialHandle,
        Func<FeishuStreamingHandle, string, CancellationToken, Task<FeishuStreamingHandle?>> replacementFactory,
        Action<FeishuStreamingHandle>? handleChanged = null,
        Func<FeishuStreamingHandle, string, CancellationToken, Task>? stoppedHandleFinalizer = null)
    {
        CurrentHandle = initialHandle;
        _replacementFactory = replacementFactory;
        _handleChanged = handleChanged ?? (_ => { });
        _stoppedHandleFinalizer = stoppedHandleFinalizer;
    }

    public FeishuStreamingHandle CurrentHandle { get; private set; }

    public async Task<bool> UpdateAsync(string content, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await CurrentHandle.UpdateAsync(content);
            if (!CurrentHandle.AreCardUpdatesStopped)
            {
                return true;
            }

            if (_replacementCount >= MaxReplacementCardsPerLogicalStream)
            {
                return false;
            }

            var stoppedHandle = CurrentHandle;
            var replacement = await _replacementFactory(stoppedHandle, content, cancellationToken);
            if (replacement == null)
            {
                return false;
            }

            if (_stoppedHandleFinalizer != null)
            {
                try
                {
                    await _stoppedHandleFinalizer(stoppedHandle, content, cancellationToken);
                }
                catch
                {
                }
            }

            _replacementCount++;
            CurrentHandle = replacement;
            _handleChanged(replacement);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> FinishAsync(string finalContent)
    {
        await _lock.WaitAsync();
        try
        {
            var finished = await CurrentHandle.FinishAsync(finalContent);
            if (finished)
            {
                return true;
            }

            if (_replacementCount >= MaxReplacementCardsPerLogicalStream)
            {
                return false;
            }

            var stoppedHandle = CurrentHandle;
            var replacement = await _replacementFactory(stoppedHandle, finalContent, CancellationToken.None);
            if (replacement == null)
            {
                return false;
            }

            _replacementCount++;
            CurrentHandle = replacement;
            _handleChanged(replacement);
            return await CurrentHandle.FinishAsync(finalContent);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SwitchHandleAsync(
        FeishuStreamingHandle handle,
        bool resetReplacementCount = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            CurrentHandle = handle;
            if (resetReplacementCount)
            {
                _replacementCount = 0;
            }

            _handleChanged(handle);
        }
        finally
        {
            _lock.Release();
        }
    }
}
