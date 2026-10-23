namespace TrafficCounter.Vision;

/// <summary>
/// A camera or video file delivering frames (ADR-0011). Sources always hand out the newest frame;
/// frames the consumer was too slow to take are dropped and counted.
/// </summary>
public interface IFrameSource : IAsyncDisposable
{
    string Description { get; }

    FrameSourceStatistics Statistics { get; }

    void Start();

    /// <summary>Waits for the next frame. The caller owns and must dispose it.</summary>
    ValueTask<Frame> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Synchronous variant for threads that must not change, e.g. a HighGUI window loop.</summary>
    bool TryRead(TimeSpan timeout, out Frame? frame);
}

/// <summary>
/// A source that hands every captured frame to an observer on its capture thread, before a frame is dropped for a
/// consumer that is too slow (e.g. reference recordings, which must not get gaps when counting stalls).
/// </summary>
public interface IObservableFrameSource
{
    /// <summary>
    /// Called on the capture thread for every frame; the frame is only valid during the call (copy what is kept). Must
    /// be quick, and exceptions are swallowed so that the capture never stops because of an observer.
    /// </summary>
    Action<Frame>? CaptureObserver { get; set; }
}

/// <summary>A device source that can be closed and opened again on request, e.g. when it delivers only black frames.</summary>
public interface IReopenableFrameSource
{
    /// <summary>Closes the device after the current read; the capture thread opens it again right away.</summary>
    void Reopen();
}

/// <param name="Captured">Frames received from the device.</param>
/// <param name="Dropped">Frames discarded because the consumer did not take them in time.</param>
/// <param name="Reconnects">Number of times the device had to be reopened.</param>
public sealed record FrameSourceStatistics(long Captured, long Dropped, int Reconnects);
