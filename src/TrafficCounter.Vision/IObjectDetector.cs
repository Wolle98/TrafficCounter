using OpenCvSharp;
using TrafficCounter.Core;

namespace TrafficCounter.Vision;

/// <summary>Finds vehicles in an image (ADR-0006). Only classes mapped to a <see cref="VehicleClass"/> are returned.</summary>
public interface IObjectDetector : IDisposable
{
    /// <summary>Name of the model, stored with each operating period.</summary>
    string ModelName { get; }

    /// <summary>Which hardware actually runs the model, e.g. "DirectML (GPU 0)" or "CPU".</summary>
    string ExecutionProvider { get; }

    /// <param name="image">Full camera frame.</param>
    /// <param name="region">Part of the frame to analyse (evaluation band); boxes are returned in frame coordinates.</param>
    IReadOnlyList<Detection> Detect(Mat image, Rect region);
}
