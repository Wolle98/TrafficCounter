using TrafficCounter.Core;
using TrafficCounter.Core.Operations;
using TrafficCounter.Data;

namespace TrafficCounter.DemoData.Tests;

public class DemoDataGeneratorTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

    // Generated once: 21 days starting on a Monday.
    private static readonly Lazy<DemoDataSet> Data = new(() => Generate());

    [Fact]
    public void Class_shares_match_the_targets()
    {
        List<VehiclePassage> passages = Data.Value.Passages;
        foreach ((VehicleClass vehicleClass, double share) in TrafficProfile.ClassShares)
        {
            double actual = (double)passages.Count(p => p.VehicleClass == vehicleClass) / passages.Count;
            Assert.InRange(actual, share * 0.85, share * 1.15);
        }
    }

    [Fact]
    public void Speed_bands_of_motor_vehicles_match_the_targets()
    {
        List<VehiclePassage> motor = MotorVehicles();
        foreach ((double from, double to, double share) in SpeedModel.MotorBands)
        {
            double actual = (double)motor.Count(p => p.SpeedKmh >= from && p.SpeedKmh < to) / motor.Count;
            Assert.InRange(actual, share - 0.012, share + 0.012);
        }
    }

    [Fact]
    public void Speeding_is_more_frequent_at_night_and_for_cars_than_for_trucks_and_buses()
    {
        List<VehiclePassage> motor = MotorVehicles();
        static double Fast(IEnumerable<VehiclePassage> passages) => passages.Average(p => p.SpeedKmh >= 55 ? 1.0 : 0.0);

        Assert.True(Fast(motor.Where(p => TrafficProfile.IsNight(Local(p.PassedAt))))
            > 2 * Fast(motor.Where(p => !TrafficProfile.IsNight(Local(p.PassedAt)))));
        Assert.True(Fast(motor.Where(p => p.VehicleClass == VehicleClass.Car))
            > 2 * Fast(motor.Where(p => p.VehicleClass is VehicleClass.Truck or VehicleClass.Bus)));
    }

    [Fact]
    public void Bicycles_and_tractors_are_slow()
    {
        List<VehiclePassage> bikes = [.. Data.Value.Passages.Where(p => p.VehicleClass == VehicleClass.Bicycle)];
        Assert.All(bikes, b => Assert.InRange(b.SpeedKmh!.Value, 10, 25));
        Assert.InRange(bikes.Average(b => b.SpeedKmh >= 15 && b.SpeedKmh < 20 ? 1.0 : 0.0), 0.8, 0.9);
        Assert.All(
            Data.Value.Passages.Where(p => p.VehicleClass == VehicleClass.Agricultural),
            t => Assert.InRange(t.SpeedKmh!.Value, 15, 40));
    }

    [Fact]
    public void Weekday_follows_the_measured_profile_and_weekends_have_no_peaks()
    {
        static bool Weekday(DateTime day) => !TrafficProfile.IsWeekend(day);
        Assert.InRange(PerHour(Weekday, new TimeOnly(7, 30), 30), 340, 440);   // morning peak ≈ 390/h
        Assert.InRange(PerHour(Weekday, new TimeOnly(13, 0), 60), 230, 300);   // school ≈ 265/h
        Assert.InRange(PerHour(Weekday, new TimeOnly(16, 30), 30), 270, 360);  // evening peak ≈ 315/h
        Assert.InRange(PerHour(Weekday, new TimeOnly(9, 0), 180), 150, 230);   // forenoon ≈ 180/h
        Assert.InRange(PerHour(Weekday, new TimeOnly(21, 0), 30), 45, 85);     // measured 63/h
        Assert.InRange(PerHour(Weekday, new TimeOnly(1, 0), 180), 0, 15);

        // A weekday has about 3,400 vehicles (the counter's own days are shorter by the incidents).
        double perWeekday = Data.Value.Passages.Count(p => Weekday(Local(p.PassedAt))) / 15.0;
        Assert.InRange(perWeekday, 3000, 3700);

        int busiestWeekendHour = Data.Value.Passages
            .Select(p => Local(p.PassedAt))
            .Where(TrafficProfile.IsWeekend)
            .GroupBy(t => (t.Date, t.Hour))
            .Max(g => g.Count());
        Assert.InRange(busiestWeekendHour, 150, 280);
    }

    [Fact]
    public void Buses_keep_their_timetable_and_bicycles_rarely_ride_at_night()
    {
        List<VehiclePassage> passages = Data.Value.Passages;
        List<VehiclePassage> buses = [.. passages.Where(p => p.VehicleClass == VehicleClass.Bus)];
        Assert.All(buses, b => Assert.InRange(Local(b.PassedAt).Hour, 6, 19));
        Assert.All(buses.Where(b => b.Direction == TravelDirection.A), b => Assert.InRange(Local(b.PassedAt).Minute, 13, 17));
        Assert.All(buses.Where(b => b.Direction == TravelDirection.B), b => Assert.InRange(Local(b.PassedAt).Minute, 43, 48));

        // One per direction and hour on weekdays 06–19 (a few lost to the incidents).
        int weekdayBuses = buses.Count(b => !TrafficProfile.IsWeekend(Local(b.PassedAt)));
        Assert.InRange(weekdayBuses, (15 * 28) - 12, 15 * 28);

        int bikes = passages.Count(p => p.VehicleClass == VehicleClass.Bicycle);
        int nightBikes = passages.Count(p => p.VehicleClass == VehicleClass.Bicycle && Local(p.PassedAt).Hour < 5);
        Assert.True(nightBikes < bikes * 0.01);
    }

    [Fact]
    public void Direction_a_has_about_44_percent_every_day()
    {
        foreach (IGrouping<DateTime, VehiclePassage> day in Data.Value.Passages.GroupBy(p => Local(p.PassedAt).Date))
        {
            Assert.InRange(day.Average(p => p.Direction == TravelDirection.A ? 1.0 : 0.0), 0.36, 0.53);
        }
    }

    [Fact]
    public void Line_positions_follow_the_measured_lanes()
    {
        List<VehiclePassage> cars = [.. Data.Value.Passages.Where(p => p.VehicleClass == VehicleClass.Car)];
        Assert.InRange(cars.Where(p => p.Direction == TravelDirection.A).Average(p => p.LinePosition!.Value), 0.35, 0.39);
        Assert.InRange(cars.Where(p => p.Direction == TravelDirection.B).Average(p => p.LinePosition!.Value), 0.42, 0.46);

        List<VehiclePassage> bikes = [.. Data.Value.Passages.Where(p => p.VehicleClass == VehicleClass.Bicycle)];
        double onSidewalk = bikes.Average(p => p.LinePosition < 0.14 || p.LinePosition > 0.67 ? 1.0 : 0.0);
        Assert.InRange(onSidewalk, 0.14, 0.22);
    }

    [Fact]
    public void Nothing_is_counted_while_the_counter_or_the_camera_is_down()
    {
        DemoDataSet data = Data.Value;
        Assert.Equal(3, data.Periods.Count);
        Assert.Equal(PeriodEndReason.Shutdown, data.Periods[0].EndReason);
        Assert.Equal(PeriodEndReason.Unclean, data.Periods[1].EndReason);
        for (int i = 0; i + 1 < data.Periods.Count; i++)
        {
            DateTimeOffset gapStart = data.Periods[i].EndedAt!.Value;
            DateTimeOffset gapEnd = data.Periods[i + 1].StartedAt;
            Assert.True(gapEnd > gapStart);
            Assert.DoesNotContain(data.Passages, p => p.PassedAt >= gapStart && p.PassedAt < gapEnd);
        }

        List<MinuteStatistic> lost = [.. data.Minutes.Where(m => m.SourceState == SourceState.Unavailable)];
        Assert.Equal(28, lost.Count);
        Assert.All(lost, m => Assert.Equal(0, m.Passages));
        Assert.Equal(data.Passages.Count, data.Minutes.Sum(m => m.Passages));
        Assert.Contains(data.Events, e => e.Kind == SystemEventKind.UncleanShutdownDetected);
        Assert.Contains(data.Minutes, m => m.Quality == QualityLevel.Unusable);
        Assert.Contains(data.DriftMeasurements, d => d.Status == 2);
    }

    [Fact]
    public void Generation_is_deterministic_for_a_seed()
    {
        DemoDataSet again = Generate();
        Assert.Equal(Data.Value.Passages.Count, again.Passages.Count);
        Assert.Equal(Data.Value.Passages[^1].SpeedKmh, again.Passages[^1].SpeedKmh);
    }

    [Theory]
    [InlineData("Server=localhost;Database=TrafficCounter;Integrated Security=true", false)]
    [InlineData("Server=localhost;Database=TrafficCounter_Demo;Integrated Security=true", true)]
    public void Only_demo_databases_are_overwritten(string connection, bool allowed)
    {
        Exception? error = Record.Exception(() => DemoDataWriter.EnsureDemoDatabase(connection));
        Assert.Equal(allowed, error is null);
    }

    private static DemoDataSet Generate() =>
        new DemoDataGenerator(new DemoDataOptions { FirstDay = new DateOnly(2026, 9, 7), TimeZone = Berlin }).Generate();

    private static DateTime Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Berlin).DateTime;

    private static List<VehiclePassage> MotorVehicles() =>
        [.. Data.Value.Passages.Where(p => SpeedModel.IsMotorVehicle(p.VehicleClass!.Value))];

    private static double PerHour(Func<DateTime, bool> day, TimeOnly from, int minutes)
    {
        List<DateTime> local = [.. Data.Value.Passages.Select(p => Local(p.PassedAt)).Where(day)];
        int days = local.Select(t => t.Date).Distinct().Count();
        int count = local.Count(t => TimeOnly.FromDateTime(t) >= from && TimeOnly.FromDateTime(t) < from.AddMinutes(minutes));
        return count / (double)days * 60 / minutes;
    }
}
