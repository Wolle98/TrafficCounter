namespace TrafficCounter.Core.Operations;

/// <summary>Solar elevation after the NOAA solar calculator (accuracy well below 1° for 1800–2100).</summary>
public static class SunPosition
{
    public static double ElevationDegrees(DateTimeOffset time, double latitude, double longitude)
    {
        DateTime utc = time.UtcDateTime;
        double julianDay = utc.ToOADate() + 2415018.5;
        double century = (julianDay - 2451545) / 36525;

        double meanLongitude = Mod(280.46646 + (century * (36000.76983 + (century * 0.0003032))), 360);
        double meanAnomaly = 357.52911 + (century * (35999.05029 - (0.0001537 * century)));
        double eccentricity = 0.016708634 - (century * (0.000042037 + (0.0000001267 * century)));
        double center =
            (Sin(meanAnomaly) * (1.914602 - (century * (0.004817 + (0.000014 * century)))))
            + (Sin(2 * meanAnomaly) * (0.019993 - (0.000101 * century)))
            + (Sin(3 * meanAnomaly) * 0.000289);
        double apparentLongitude = meanLongitude + center - 0.00569 - (0.00478 * Sin(125.04 - (1934.136 * century)));
        double meanObliquity = 23 + ((26 + ((21.448 - (century * (46.815 + (century * (0.00059 - (century * 0.001813)))))) / 60)) / 60);
        double obliquity = meanObliquity + (0.00256 * Cos(125.04 - (1934.136 * century)));
        double declination = Degrees(Math.Asin(Sin(obliquity) * Sin(apparentLongitude)));

        double y = Math.Pow(Math.Tan(Radians(obliquity / 2)), 2);
        double equationOfTime = 4 * Degrees(
            (y * Sin(2 * meanLongitude))
            - (2 * eccentricity * Sin(meanAnomaly))
            + (4 * eccentricity * y * Sin(meanAnomaly) * Cos(2 * meanLongitude))
            - (0.5 * y * y * Sin(4 * meanLongitude))
            - (1.25 * eccentricity * eccentricity * Sin(2 * meanAnomaly)));

        double trueSolarMinutes = Mod(utc.TimeOfDay.TotalMinutes + equationOfTime + (4 * longitude), 1440);
        double hourAngle = (trueSolarMinutes / 4) - 180;
        double cosZenith = (Sin(latitude) * Sin(declination)) + (Cos(latitude) * Cos(declination) * Cos(hourAngle));
        return 90 - Degrees(Math.Acos(Math.Clamp(cosZenith, -1, 1)));
    }

    private static double Sin(double degrees) => Math.Sin(Radians(degrees));

    private static double Cos(double degrees) => Math.Cos(Radians(degrees));

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static double Degrees(double radians) => radians * 180 / Math.PI;

    private static double Mod(double value, double modulus) => ((value % modulus) + modulus) % modulus;
}

/// <summary>
/// Classifies lighting from the sun's elevation (ADR-0017): day above 0°, dusk during civil twilight
/// (0° to -6°), night below. Coarse site coordinates (one decimal) are sufficient.
/// </summary>
public sealed class LightingClassifier(double? latitude, double? longitude)
{
    public const double CivilTwilightDegrees = -6;

    public LightingCondition Classify(DateTimeOffset time)
    {
        if (latitude is not double lat || longitude is not double lon)
        {
            return LightingCondition.Unknown;
        }

        double elevation = SunPosition.ElevationDegrees(time, lat, lon);
        return elevation > 0 ? LightingCondition.Day
            : elevation > CivilTwilightDegrees ? LightingCondition.Dusk
            : LightingCondition.Night;
    }
}
