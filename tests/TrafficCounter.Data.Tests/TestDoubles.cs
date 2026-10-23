using TrafficCounter.Core;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data.Tests;

internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>In-memory store that can simulate an unreachable database.</summary>
internal sealed class FakeStore : ITrafficStore
{
    public List<StoreRecord> Records { get; } = [];

    public bool Available { get; set; } = true;

    public int WriteCalls { get; private set; }

    public Task<int> EnsureCameraAsync(CameraRegistration camera, CancellationToken cancellationToken) => Task.FromResult(1);

    public Task<(int PeriodId, int UncleanPeriodsClosed)> StartPeriodAsync(int cameraId, OperatingPeriodStart start, CancellationToken cancellationToken) =>
        Task.FromResult((1, 0));

    public Task EndPeriodAsync(int periodId, DateTimeOffset endedAt, PeriodEndReason reason, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<(int Hour, float Contrast, float Sharpness, int Samples)>> GetQualityHistoryAsync(
        int cameraId, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(int, float, float, int)>>([]);

    /// <summary>Makes exactly this write call fail (1-based), e.g. to interrupt a replay in the middle.</summary>
    public int? FailOnCall { get; set; }

    public Task WriteAsync(IReadOnlyList<StoreRecord> records, CancellationToken cancellationToken)
    {
        WriteCalls++;
        if (!Available || WriteCalls == FailOnCall)
        {
            throw new TimeoutException("database down");
        }

        Records.AddRange(records);
        return Task.CompletedTask;
    }
}

internal static class Samples
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 28, 18, 0, 0, TimeSpan.FromHours(2));

    public static PassageRecord Passage(int trackId, int periodId = 1) =>
        new(1, periodId, new VehiclePassageEvent(T0.AddSeconds(trackId), TravelDirection.A, VehicleClass.Car, 0.9f, null, trackId, 25, 0.4f, TrailerCount: 1));

    public static MinuteStatistics Minute(int minute, int periodId = 1) =>
        new(1, periodId, T0.AddMinutes(minute), 1500, 1490, 10, 12, 3, SourceState.Ok, QualityLevel.Good, 90, 40, 300, LightingCondition.Day);
}
