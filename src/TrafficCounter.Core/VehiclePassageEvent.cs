namespace TrafficCounter.Core;

/// <summary>A vehicle that crossed the counting line. Contains no image data (ADR-0005).</summary>
/// <param name="PassedAt">Moment the vehicle crossed the line.</param>
/// <param name="ClassConfidence">Share of the class vote won by <paramref name="Class"/> (0..1).</param>
/// <param name="SpeedKmh">Estimated speed, available from stage 5.</param>
/// <param name="LinePosition">
/// Where along the counting line the vehicle crossed: 0 at its start, 1 at its end.
/// Lets reports tell lanes and sidewalks apart without fixing zones in the schema (ADR-0020).
/// </param>
/// <param name="TrailerCount">Trailers towed by the vehicle; they are not counted as vehicles of their own (ADR-0013).</param>
/// <param name="Track">How the track was observed, for the log (e.g. why a passage has no speed); not stored.</param>
public sealed record VehiclePassageEvent(
    DateTimeOffset PassedAt,
    TravelDirection Direction,
    VehicleClass Class,
    float? ClassConfidence,
    float? SpeedKmh,
    int TrackId,
    int TrackLengthFrames,
    float LinePosition,
    int TrailerCount = 0,
    TrackObservation? Track = null);

/// <summary>How a counted track was observed: the basis of its speed estimate and where it was seen.</summary>
/// <param name="SpeedSamples">Positions inside the speed measuring zone around the counting line.</param>
/// <param name="SpeedSeconds">Time between the first and the last of those positions.</param>
/// <param name="TrackSeconds">Time between the first and the last position of the whole track.</param>
/// <param name="FirstDistance">Distance from the counting line (pixels, signed) where the track was first seen.</param>
/// <param name="LastDistance">Distance from the counting line where it was last seen.</param>
public sealed record TrackObservation(int SpeedSamples, double SpeedSeconds, double TrackSeconds, float FirstDistance, float LastDistance);
