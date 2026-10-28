using TrafficCounter.Core;

namespace TrafficCounter.Counting;

/// <summary>Why a track ended without being counted.</summary>
public enum UncountedReason
{
    /// <summary>The track ended before it crossed the counting line (lost under occlusion, left the band).</summary>
    NotCrossed,

    /// <summary>It crossed, but with fewer frames than <see cref="CountingSettings.MinTrackFrames"/>.</summary>
    TooShort,

    /// <summary>It crossed, then backed up and ended on its original side.</summary>
    TurnedBack,

    /// <summary>It was a trailer of another vehicle (ADR-0047).</summary>
    Trailer,

    /// <summary>It was taken for a second track of a vehicle already counted (<see cref="CountingSettings.DuplicateSeconds"/>).</summary>
    Duplicate,

    /// <summary>It ended just behind the line, closer than <see cref="CountingSettings.MinEndBeyondPixels"/>.</summary>
    EndedAtLine,
}

/// <summary>
/// A track that ended without being counted, to explain missed vehicles in the debug log and the regression.
/// Distances are signed pixels from the counting line, like <see cref="TrackObservation"/>.
/// </summary>
public sealed record UncountedTrack(
    int TrackId,
    VehicleClass Class,
    int Frames,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    float FirstDistance,
    float LastDistance,
    UncountedReason Reason)
{
    /// <summary>
    /// True if the track came near enough to the line to matter: it crossed, or it ended at least a fifth closer to
    /// the line than it started. Parked cars and tracks moving away are left out.
    /// </summary>
    public bool Approached => Reason != UncountedReason.NotCrossed
        || (Frames > 1 && MathF.Sign(FirstDistance) == MathF.Sign(LastDistance) && MathF.Abs(LastDistance) <= 0.8f * MathF.Abs(FirstDistance));
}
