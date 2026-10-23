using Microsoft.Extensions.Logging.Abstractions;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data.Tests;

public sealed class BufferedStoreWriterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tc-writer-").FullName;
    private readonly FakeStore _store = new();
    private readonly ManualTime _time = new(Samples.T0);

    private string FallbackPath => Path.Combine(_directory, "fallback.jsonl");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private BufferedStoreWriter CreateWriter() =>
        new(_store, FallbackPath, NullLogger<BufferedStoreWriter>.Instance, _time,
            new BufferedStoreWriterOptions { BatchSize = 3, RetryInterval = TimeSpan.FromSeconds(30) });

    /// <summary>Runs one writer session: start, enqueue, stop (which drains the queue).</summary>
    private async Task SessionAsync(params StoreRecord[] records)
    {
        await using BufferedStoreWriter writer = CreateWriter();
        writer.Start();
        foreach (StoreRecord record in records)
        {
            writer.Enqueue(record);
        }
    }

    [Fact]
    public async Task Records_are_written_in_batches()
    {
        await SessionAsync(Samples.Passage(1), Samples.Passage(2), Samples.Passage(3), Samples.Passage(4), Samples.Minute(0));

        Assert.Equal(5, _store.Records.Count);
        Assert.False(File.Exists(FallbackPath));
    }

    [Fact]
    public async Task Records_go_to_the_fallback_file_while_the_database_is_down()
    {
        _store.Available = false;

        await SessionAsync(Samples.Passage(1), Samples.Minute(0));

        Assert.Empty(_store.Records);
        string[] lines = File.ReadAllLines(FallbackPath);
        Assert.Equal(3, lines.Length); // two records plus the "storage unavailable" event
        Assert.Contains("\"$type\":\"passage\"", lines[0]);
        Assert.Contains(nameof(SystemEventKind.StorageUnavailable), string.Join('\n', lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_is_not_retried_before_the_retry_interval()
    {
        await using BufferedStoreWriter writer = CreateWriter();
        writer.Start();
        _store.Available = false;
        writer.Enqueue(Samples.Passage(1));
        await WaitUntil(() => File.Exists(FallbackPath));
        int callsAfterFailure = _store.WriteCalls;

        writer.Enqueue(Samples.Passage(2));
        await WaitUntil(() => writer.Buffered >= 3);

        Assert.Equal(callsAfterFailure, _store.WriteCalls);
    }

    [Fact]
    public async Task Locked_fallback_file_does_not_lose_records()
    {
        _store.Available = false;
        await using (BufferedStoreWriter writer = CreateWriter())
        {
            writer.Start();
            writer.Enqueue(Samples.Passage(1));
            await WaitUntil(() => writer.Buffered >= 1);

            // Someone opens the file exclusively while more records arrive.
            using (new FileStream(FallbackPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                writer.Enqueue(Samples.Passage(2));
                await Task.Delay(2000, TestContext.Current.CancellationToken);
            }

            writer.Enqueue(Samples.Passage(3));
        }

        _store.Available = true;
        await SessionAsync(Samples.Passage(4));

        Assert.Equal([1, 2, 3, 4], _store.Records.OfType<PassageRecord>().Select(p => p.Passage.TrackId));
    }

    [Fact]
    public async Task Buffered_records_are_replayed_when_the_database_is_back()
    {
        _store.Available = false;
        await SessionAsync(Samples.Passage(1), Samples.Minute(0)); // e.g. before a restart

        _store.Available = true;
        await SessionAsync(Samples.Passage(2));

        Assert.False(File.Exists(FallbackPath));
        Assert.Equal([1, 2], _store.Records.OfType<PassageRecord>().Select(p => p.Passage.TrackId));
        Assert.Single(_store.Records.OfType<MinuteStatistics>());
        Assert.Contains(_store.Records.OfType<SystemEventRecord>(), e => e.Kind == SystemEventKind.StorageUnavailable);
    }

    [Fact]
    public async Task Interrupted_replay_continues_without_duplicates()
    {
        // Seven buffered passages plus the "unavailable" event: three chunks of three at batch size 3.
        _store.Available = false;
        await SessionAsync([.. Enumerable.Range(1, 7).Select(i => (StoreRecord)Samples.Passage(i))]);

        // The database comes back, but fails again while the second chunk of the replay is written.
        _store.Available = true;
        _store.FailOnCall = _store.WriteCalls + 2;
        await SessionAsync();
        Assert.Equal([1, 2, 3], _store.Records.OfType<PassageRecord>().Select(p => p.Passage.TrackId));

        _store.FailOnCall = null;
        _time.Now += TimeSpan.FromMinutes(1);
        await SessionAsync(Samples.Passage(8));

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], _store.Records.OfType<PassageRecord>().Select(p => p.Passage.TrackId));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Recovery_within_a_session_is_recorded_as_event()
    {
        await using (BufferedStoreWriter writer = CreateWriter())
        {
            writer.Start();
            _store.Available = false;
            writer.Enqueue(Samples.Passage(1));
            await WaitUntil(() => File.Exists(FallbackPath));

            _store.Available = true;
            _time.Now += TimeSpan.FromSeconds(31);
            writer.Enqueue(Samples.Passage(2));
            await WaitUntil(() => writer.StorageAvailable);
        }

        Assert.Equal([1, 2], _store.Records.OfType<PassageRecord>().Select(p => p.Passage.TrackId));
        Assert.Contains(_store.Records.OfType<SystemEventRecord>(), e => e.Kind == SystemEventKind.StorageRecovered);
        Assert.False(File.Exists(FallbackPath));
    }

    [Fact]
    public async Task All_record_types_survive_the_fallback_file()
    {
        StoreRecord[] records =
        [
            Samples.Passage(1),
            Samples.Minute(0),
            new SystemEventRecord(1, 1, Samples.T0, SystemEventKind.QualityChanged, EventSeverity.Warning, "Image quality Degraded."),
            new DriftRecord(1, 1, Samples.T0, 2, 12.5f, -7f, -0.3f, 1.001f, 240, 0.8f),
        ];
        _store.Available = false;
        await SessionAsync(records);

        _store.Available = true;
        await SessionAsync();

        Assert.Contains(records[0], _store.Records);
        Assert.Contains(records[1], _store.Records);
        Assert.Contains(records[2], _store.Records);
        Assert.Contains(records[3], _store.Records);
    }

    [Fact]
    public async Task Truncated_line_in_fallback_file_is_skipped()
    {
        _store.Available = false;
        await SessionAsync(Samples.Passage(1));
        File.AppendAllText(FallbackPath, "{\"$type\":\"passage\",\"Camera");

        _store.Available = true;
        await SessionAsync(Samples.Passage(2));

        Assert.Equal([1, 2], _store.Records.OfType<PassageRecord>().Select(p => p.Passage.TrackId));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), "condition not reached in time");
    }
}
