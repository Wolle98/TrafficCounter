namespace TrafficCounter.Counting;

/// <summary>
/// The normal rate at which the source delivers frames, from the capture sequence numbers: the median over the last
/// five minutes of the rates of ten-second stretches (ADR-0063). A stalled stream of half a minute hardly moves it,
/// a camera that slows down at night (longer exposure) is followed within minutes. Not thread-safe.
/// </summary>
public sealed class CaptureRate
{
    /// <summary>Length of one stretch whose rate enters the median.</summary>
    public const double StretchSeconds = 10;

    /// <summary>Number of stretches kept (five minutes).</summary>
    public const int Stretches = 30;

    /// <summary>Fewer stretches give no normal rate yet (after a start or a new source).</summary>
    public const int MinStretches = 3;

    private readonly Queue<double> _rates = new();
    private (DateTimeOffset At, long Sequence)? _start;
    private (DateTimeOffset At, long Sequence)? _last;

    /// <summary>Notes a delivered frame: its timestamp and the capture sequence number of the source.</summary>
    public void Add(DateTimeOffset at, long sequence)
    {
        if (_last is { } last && (sequence <= last.Sequence || at <= last.At))
        {
            if (sequence >= last.Sequence && at >= last.At)
            {
                return; // the same frame again
            }

            // A new source or a replay started over: its numbers say nothing about the old rate.
            _rates.Clear();
            _start = null;
        }

        _last = (at, sequence);
        if (_start is not { } start)
        {
            _start = (at, sequence);
            return;
        }

        double seconds = (at - start.At).TotalSeconds;
        if (seconds < StretchSeconds)
        {
            return;
        }

        // A stretch much longer than wanted spans an outage of the source (reopening); its rate is not a normal one.
        if (seconds < 3 * StretchSeconds)
        {
            _rates.Enqueue((sequence - start.Sequence) / seconds);
            if (_rates.Count > Stretches)
            {
                _rates.Dequeue();
            }
        }

        _start = (at, sequence);
    }

    /// <summary>Normal frames per second, or null while fewer than <see cref="MinStretches"/> stretches are known.</summary>
    public double? PerSecond
    {
        get
        {
            if (_rates.Count < MinStretches)
            {
                return null;
            }

            List<double> sorted = [.. _rates.Order()];
            return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
        }
    }
}
