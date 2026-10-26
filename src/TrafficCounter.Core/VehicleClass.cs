namespace TrafficCounter.Core;

/// <summary>Target vehicle classes (ADR-0013). Values are persisted – never renumber.</summary>
public enum VehicleClass : byte
{
    Unknown = 0,
    Car = 1,
    Van = 2,
    Truck = 3,
    Bus = 4,
    Motorcycle = 5,
    Bicycle = 6,

    /// <summary>Agricultural machine, usually a tractor, often with one or two trailers (counted as one vehicle).</summary>
    Agricultural = 7,

    /// <summary>
    /// E-scooter with its standing rider. Only in hand counts so far: the current (COCO) model does not detect
    /// e-scooters at all, not even as bicycles (none of 44 in seven hand counts up to 2026-10-20). The own model is
    /// meant to learn them (ADR-0039).
    /// </summary>
    EScooter = 8,

    /// <summary>
    /// A trailer detected on its own by the own model. Only inside the counting: it is tracked so the counting engine
    /// can attach it to the vehicle in front (that passage's trailer count), and it is never counted or stored as a
    /// vehicle of its own (ADR-0047).
    /// </summary>
    Trailer = 9,
}
