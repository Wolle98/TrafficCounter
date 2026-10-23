using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>A captured image with its capture time. The receiver owns the frame and must dispose it.</summary>
public sealed class Frame(Mat image, DateTimeOffset timestamp, long sequence) : IDisposable
{
    public Mat Image { get; } = image;

    /// <summary>Time the frame was received from the camera.</summary>
    public DateTimeOffset Timestamp { get; } = timestamp;

    /// <summary>Running number assigned by the source; gaps mean frames were dropped.</summary>
    public long Sequence { get; } = sequence;

    public void Dispose() => Image.Dispose();
}
