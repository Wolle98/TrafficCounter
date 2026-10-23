using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Vision;

/// <summary>
/// A detector that also reports people, only so that they can be made unrecognisable in stored training samples
/// (ADR-0026). People are never counted or tracked.
/// </summary>
public interface IPersonDetections
{
    /// <summary>Person boxes of the last <see cref="IObjectDetector.Detect"/> call, in frame coordinates.</summary>
    IReadOnlyList<BoundingBox> LastPersons { get; }
}
