namespace TrafficCounter.Evaluation;

/// <summary>
/// Converts the speedometer reading of the test vehicle into its true speed (ADR-0035): true = Factor · reading +
/// OffsetKmh, fitted from speedometer against GPS. Speedometers read high, more so at low speeds.
/// </summary>
public sealed record SpeedometerCalibration(double Factor = 1, double OffsetKmh = 0)
{
    public static SpeedometerCalibration None { get; } = new();

    public bool IsIdentity => Factor == 1 && OffsetKmh == 0;

    public float ToTrue(float reading) => (float)((Factor * reading) + OffsetKmh);

    public float ToReading(float trueSpeed) => (float)((trueSpeed - OffsetKmh) / Factor);
}
