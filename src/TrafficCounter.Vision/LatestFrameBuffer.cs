using System.Diagnostics.CodeAnalysis;

namespace TrafficCounter.Vision;

/// <summary>
/// Single-slot hand-over between a capture thread and a consumer. Publishing replaces (and disposes)
/// a frame that has not been taken yet, so latency never builds up when processing is slower than the camera.
/// </summary>
public sealed class LatestFrameBuffer : IDisposable
{
    private readonly Lock _lock = new();

    // Invariant: the semaphore count is 1 exactly while _pending is set.
    private readonly SemaphoreSlim _available = new(0, 1);
    private Frame? _pending;
    private long _published;
    private long _dropped;
    private bool _disposed;

    public long Published => Interlocked.Read(ref _published);

    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Sees every published frame before it may be replaced; see <see cref="IObservableFrameSource"/>.</summary>
    public Action<Frame>? Observer { get; set; }

    public void Publish(Frame frame)
    {
        try
        {
            Observer?.Invoke(frame);
        }
        catch (Exception)
        {
            // An observer must never stop the capture.
        }

        bool signal;
        lock (_lock)
        {
            if (_disposed)
            {
                frame.Dispose();
                return;
            }

            signal = _pending is null;
            if (_pending is not null)
            {
                _pending.Dispose();
                Interlocked.Increment(ref _dropped);
            }

            _pending = frame;
            Interlocked.Increment(ref _published);
        }

        if (signal)
        {
            _available.Release();
        }
    }

    public async ValueTask<Frame> TakeAsync(CancellationToken cancellationToken)
    {
        await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
        return TakePending();
    }

    public bool TryTake(TimeSpan timeout, [NotNullWhen(true)] out Frame? frame)
    {
        if (!_available.Wait(timeout))
        {
            frame = null;
            return false;
        }

        frame = TakePending();
        return true;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _pending?.Dispose();
            _pending = null;
        }

        _available.Dispose();
    }

    private Frame TakePending()
    {
        lock (_lock)
        {
            Frame frame = _pending ?? throw new InvalidOperationException("Frame buffer signalled without a pending frame.");
            _pending = null;
            return frame;
        }
    }
}
