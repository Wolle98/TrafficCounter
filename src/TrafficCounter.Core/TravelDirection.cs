namespace TrafficCounter.Core;

/// <summary>
/// Direction in which a vehicle crossed the counting line (ADR-0009). Values are persisted – never renumber.
/// Human-readable names per direction are part of the site configuration.
/// </summary>
public enum TravelDirection : byte
{
    Unknown = 0,

    /// <summary>Crossed from the left-hand to the right-hand side of the counting line (looking from Start to End).</summary>
    A = 1,

    /// <summary>Crossed from the right-hand to the left-hand side of the counting line.</summary>
    B = 2,
}
