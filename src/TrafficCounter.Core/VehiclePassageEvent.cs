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
public sealed record VehiclePassageEvent(
    DateTimeOffset PassedAt,
    TravelDirection Direction,
    VehicleClass Class,
    float? ClassConfidence,
    float? SpeedKmh,
    int TrackId,
    int TrackLengthFrames,
    float LinePosition,
    int TrailerCount = 0);
