using TrafficCounter.Core;

namespace TrafficCounter.DemoData;

/// <summary>
/// Speeds in a 30 km/h zone. Motor vehicles: 80 % at 28–35 km/h, 10 % at 35–45, 5 % at 45–55, 5 % at 55–70.
/// Speeding is more frequent at night and for cars, vans and motorcycles than for trucks and buses; the band
/// probabilities are calibrated over the generated vehicles so that the overall shares stay as specified.
/// Tractors 15–40 km/h, bicycles and e-scooters mostly 15–20 km/h, rarely up to 25 km/h.
/// </summary>
public sealed class SpeedModel
{
    public static readonly (double From, double To, double Share)[] MotorBands =
    [
        (28, 35, 0.80),
        (35, 45, 0.10),
        (45, 55, 0.05),
        (55, 70, 0.05),
    ];

    private readonly double[] _meanWeight;

    /// <param name="motorVehicles">All motor vehicles of the period; used to keep the overall band shares.</param>
    public SpeedModel(IEnumerable<(VehicleClass Class, DateTime Local)> motorVehicles)
    {
        _meanWeight = new double[MotorBands.Length];
        int count = 0;
        foreach ((VehicleClass vehicleClass, DateTime local) in motorVehicles)
        {
            count++;
            for (int band = 1; band < MotorBands.Length; band++)
            {
                _meanWeight[band] += SpeedingWeight(band, vehicleClass, local);
            }
        }

        for (int band = 1; band < MotorBands.Length; band++)
        {
            _meanWeight[band] = count > 0 ? _meanWeight[band] / count : 1;
        }
    }

    public static bool IsMotorVehicle(VehicleClass vehicleClass) =>
        vehicleClass is VehicleClass.Car or VehicleClass.Van or VehicleClass.Truck or VehicleClass.Bus or VehicleClass.Motorcycle;

    public double Draw(Random random, VehicleClass vehicleClass, DateTime local)
    {
        switch (vehicleClass)
        {
            case VehicleClass.Bicycle:
                double r = random.NextDouble();
                return r < 0.85 ? Triangular(random, 15, 20, 17.5)
                    : r < 0.97 ? Triangular(random, 20, 25, 20)
                    : Triangular(random, 10, 15, 15);
            case VehicleClass.Agricultural:
                return Triangular(random, 15, 40, 25);
            case VehicleClass.Unknown:
                return Triangular(random, 20, 40, 31);
        }

        double u = random.NextDouble();
        double cumulative = 0;
        for (int band = MotorBands.Length - 1; band >= 1; band--)
        {
            cumulative += MotorBands[band].Share * SpeedingWeight(band, vehicleClass, local) / _meanWeight[band];
            if (u < cumulative)
            {
                return MotorBands[band].From + (random.NextDouble() * (MotorBands[band].To - MotorBands[band].From));
            }
        }

        return Triangular(random, MotorBands[0].From, MotorBands[0].To, 32);
    }

    /// <summary>Relative tendency to drive in speeding band <paramref name="band"/> (1..3).</summary>
    private static double SpeedingWeight(int band, VehicleClass vehicleClass, DateTime local)
    {
        double byClass = vehicleClass switch
        {
            VehicleClass.Motorcycle => 1.4,
            VehicleClass.Truck => 0.3,
            VehicleClass.Bus => 0.2,
            _ => 1,
        };

        // The higher the band, the stronger it is concentrated at night.
        double byTime = TrafficProfile.IsNight(local) ? band switch { 1 => 1.5, 2 => 2.5, _ => 4 }
            : local.Hour is 5 or 20 or 21 ? band switch { 1 => 1.2, 2 => 1.5, _ => 2 }
            : TrafficProfile.IsSchoolPeak(local) ? 0.4
            : 1;
        return byClass * byTime;
    }

    private static double Triangular(Random random, double min, double max, double mode)
    {
        double u = random.NextDouble();
        double split = (mode - min) / (max - min);
        return u < split
            ? min + Math.Sqrt(u * (max - min) * (mode - min))
            : max - Math.Sqrt((1 - u) * (max - min) * (max - mode));
    }
}
