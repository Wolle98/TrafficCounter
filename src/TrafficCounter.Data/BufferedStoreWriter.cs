using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data;

public sealed class BufferedStoreWriterOptions
{
    public int BatchSize { get; set; } = 200;

    /// <summary>How long to wait before retrying the database after a failure.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Decouples the pipeline from the database (ADR-0010, ADR-0024): records are queued without blocking and written
/// in batches. While the database is unreachable they go to a JSONL fallback file, which is replayed automatically
/// once the database is back â€“ nothing is lost across outages or restarts.
/// </summary>
public sealed class BufferedStoreWriter : IAsyncDisposable
{
    // Enum names keep the fallback file readable when inspected by hand.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ITrafficStore _store;
    private readonly string _fallbackPath;
    private readonly ILogger<BufferedStoreWriter> _logger;
    private readonly TimeProvider _time;
    private readonly BufferedStoreWriterOptions _options;
    // Upper bound for records held in memory when neither database nor fallback file accept them (~1 day of minutes).
    private const int MaxBacklog = 100_000;

    private readonly Channel<StoreRecord> _channel = Channel.CreateUnbounded<StoreRecord>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<StoreRecord> _backlog = [];
    private Task? _loop;
    private bool _storageAvailable = true;
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
    private long _written;
    private long _buffered;

    public BufferedStoreWriter(
        ITrafficStore store,
        string fallbackPath,
        ILogger<BufferedStoreWriter> logger,
        TimeProvider? timeProvider = null,
        BufferedStoreWriterOptions? options = null)
    {
        _store = store;
        _fallbackPath = fallbackPath;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _options = options ?? new BufferedStoreWriterOptions();
    }

    /// <summary>Records written to the database (including replayed ones).</summary>
    public long Written => Interlocked.Read(ref _written);

    /// <summary>Records put into the fallback file so far.</summary>
    public long Buffered => Interlocked.Read(ref _buffered);

    public bool StorageAvailable => Volatile.Read(ref _storageAvailable);

    public void Start()
    {
        if (_loop is not null)
        {
            throw new InvalidOperationException("Writer already started.");
        }

        _loop = Task.Run(RunAsync);
    }

    /// <summary>Queues a record; never blocks.</summary>
    public void Enqueue(StoreRecord record)
    {
        if (!_channel.Writer.TryWrite(record))
        {
            _logger.LogError("Record {Record} could not be queued, writer is stopped", record);
        }
    }

    /// <summary>Stops accepting records and writes everything still queued (or puts it into the fallback file).</summary>
    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }
    }

    private async Task RunAsync()
    {
        // Replay what a previous run could not write, without waiting for the first new record.
        try
        {
            await ReplayFallbackAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Buffered records could not be replayed yet");
        }

        ChannelReader<StoreRecord> reader = _channel.Reader;
        List<StoreRecord> batch = new(_options.BatchSize);
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (batch.Count < _options.BatchSize && reader.TryRead(out StoreRecord? record))
            {
                batch.Add(record);
            }

            try
            {
                await WriteAsync(batch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The loop must survive anything; the records stay in memory and are retried with the next batch.
                _logger.LogError(ex, "Unexpected error while writing {Count} records; keeping them in memory", batch.Count);
                _backlog.AddRange(batch);
            }

            batch.Clear();
        }

        if (_backlog.Count > 0)
        {
            await AppendToFallbackAsync([]).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(List<StoreRecord> batch)
    {
        if (!_storageAvailable && _time.GetUtcNow() < _nextAttempt)
        {
            await AppendToFallbackAsync(batch).ConfigureAwait(false);
            return;
        }

        try
        {
            try
            {
                await ReplayFallbackAsync().ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                // E.g. the file is open in an editor. Replay later; this is not a database problem.
                _logger.LogWarning(ex, "Fallback file {Path} not accessible, replay postponed", _fallbackPath);
            }

            // Records the fallback file could not take go first. The recovery event is part of the same atomic
            // batch, so a failure cannot duplicate records.
            List<StoreRecord> toWrite = [.. _backlog, .. batch];
            if (!_storageAvailable)
            {
                StoreRecord first = toWrite[0];
                toWrite.Add(new SystemEventRecord(first.CameraId, first.OperatingPeriodId, _time.GetLocalNow(),
                    SystemEventKind.StorageRecovered, EventSeverity.Information, "Database reachable again; buffered records replayed."));
            }

            await _store.WriteAsync(toWrite, CancellationToken.None).ConfigureAwait(false);
            _backlog.Clear();
            Interlocked.Add(ref _written, toWrite.Count);
            _logger.LogDebug("Wrote {Count} records", toWrite.Count);

            if (!_storageAvailable)
            {
                Volatile.Write(ref _storageAvailable, true);
                _logger.LogInformation("Database reachable again");
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or IOException))
        {
            if (_storageAvailable)
            {
                _logger.LogWarning(ex, "Database not reachable, buffering to {Path}", _fallbackPath);
                StoreRecord first = batch[0];
                batch.Add(new SystemEventRecord(first.CameraId, first.OperatingPeriodId, _time.GetLocalNow(),
                    SystemEventKind.StorageUnavailable, EventSeverity.Warning, $"Database not reachable: {ex.GetType().Name}: {ex.Message}"));
            }
            else
            {
                _logger.LogDebug(ex, "Database still not reachable");
            }

            Volatile.Write(ref _storageAvailable, false);
            _nextAttempt = _time.GetUtcNow() + _options.RetryInterval;
            await AppendToFallbackAsync(batch).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Appends the in-memory backlog and <paramref name="batch"/> to the fallback file. If the file stays locked
    /// (e.g. opened by a person), the records are kept in memory and retried later.
    /// </summary>
    private async Task AppendToFallbackAsync(List<StoreRecord> batch)
    {
        List<StoreRecord> records = [.. _backlog, .. batch];
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_fallbackPath))!);
                await using (var writer = new StreamWriter(_fallbackPath, append: true))
                {
                    foreach (StoreRecord record in records)
                    {
                        await writer.WriteLineAsync(JsonSerializer.Serialize(record, JsonOptions)).ConfigureAwait(false);
                    }
                }

                _backlog.Clear();
                Interlocked.Add(ref _buffered, records.Count);
                return;
            }
            catch (IOException ex) when (attempt < 5)
            {
                _logger.LogDebug(ex, "Fallback file busy, attempt {Attempt}", attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt)).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Fallback file {Path} not writable; keeping {Count} records in memory", _fallbackPath, records.Count);
            }
        }

        _backlog.Clear();
        _backlog.AddRange(records.Count > MaxBacklog ? records[^MaxBacklog..] : records);
    }

    private string ReplayingPath => _fallbackPath + ".replaying";

    private string CursorPath => _fallbackPath + ".cursor";

    /// <summary>
    /// Writes the fallback file to the database chunk by chunk without ever replaying a chunk twice, also across a
    /// crash. The file is first renamed to <c>.replaying</c> (records buffered meanwhile go to a new fallback file),
    /// then streamed; after each chunk the number of lines done is written atomically to a <c>.cursor</c> file,
    /// from which an interrupted replay continues. Rewriting the remaining file after every chunk instead would
    /// write the data quadratically often after a long outage.
    /// </summary>
    private async Task ReplayFallbackAsync()
    {
        // A cursor without its replaying file is left over from a crash right after the last delete.
        if (!File.Exists(ReplayingPath) && File.Exists(CursorPath))
        {
            File.Delete(CursorPath);
        }

        if (File.Exists(ReplayingPath))
        {
            await ReplayFileAsync().ConfigureAwait(false);
        }

        if (File.Exists(_fallbackPath))
        {
            File.Move(_fallbackPath, ReplayingPath);
            await ReplayFileAsync().ConfigureAwait(false);
        }
    }

    private async Task ReplayFileAsync()
    {
        long done = File.Exists(CursorPath)
            && long.TryParse(await File.ReadAllTextAsync(CursorPath).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture, out long cursor)
            ? cursor
            : 0;
        _logger.LogInformation("Replaying buffered records from {Path}{From}", ReplayingPath, done > 0 ? $", continuing after line {done}" : "");

        long line = 0;
        int replayed = 0;
        List<StoreRecord> chunk = [];
        using (var reader = new StreamReader(ReplayingPath))
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } text)
            {
                if (++line <= done || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                try
                {
                    chunk.Add(JsonSerializer.Deserialize<StoreRecord>(text, JsonOptions)!);
                }
                catch (JsonException ex)
                {
                    // A crash while appending can leave a truncated last line.
                    _logger.LogWarning(ex, "Skipping unreadable line in {Path}: {Line}", ReplayingPath, text);
                }

                if (chunk.Count >= _options.BatchSize)
                {
                    replayed += await WriteChunkAsync(chunk, line).ConfigureAwait(false);
                }
            }

            replayed += await WriteChunkAsync(chunk, line).ConfigureAwait(false);
        }

        // Replaying file first: a crash between the two deletes leaves only a stale cursor, which is discarded.
        File.Delete(ReplayingPath);
        File.Delete(CursorPath);
        _logger.LogInformation("Replayed {Count} buffered records", replayed);
    }

    /// <summary>Writes the chunk and then records <paramref name="linesDone"/> atomically in the cursor file.</summary>
    private async Task<int> WriteChunkAsync(List<StoreRecord> chunk, long linesDone)
    {
        int count = chunk.Count;
        if (count > 0)
        {
            await _store.WriteAsync(chunk, CancellationToken.None).ConfigureAwait(false);
            Interlocked.Add(ref _written, count);
            chunk.Clear();
        }

        string temporary = CursorPath + ".tmp";
        await File.WriteAllTextAsync(temporary, linesDone.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
        File.Move(temporary, CursorPath, overwrite: true);
        return count;
    }
}
