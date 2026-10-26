using TrafficCounter.Core;
using TrafficCounter.Evaluation;

namespace TrafficCounter.Annotator;

/// <summary>Counts of one class in the annotated video, per direction.</summary>
internal sealed record ClassCount(VehicleClass? Class, int RightToLeft, int LeftToRight);

/// <summary>One mark of the timeline: the rarest class annotated in that stretch, and whether a speed lies in it.</summary>
internal sealed record TimelineMark(int Bucket, VehicleClass? Class, bool HasSpeed);

/// <summary>
/// The annotations of one video (ground truth, ADR-0027; test drive speeds, ADR-0035) and the edits on them, saved
/// after every change. Independent of the window, so the behaviour of keys and buttons is the same and testable.
/// Speeds are stored in the ground truth itself; a video that only has an older separate speed file
/// (<c>.speed.csv</c>) keeps writing that file.
/// </summary>
internal sealed class AnnotationModel
{
    private readonly List<GroundTruthEntry> _entries = [];

    public AnnotationModel(string videoPath)
    {
        string groundTruth = GroundTruthFile.PathFor(videoPath);
        string speedOnly = GroundTruthFile.SpeedPathFor(videoPath);
        Path = !File.Exists(groundTruth) && File.Exists(speedOnly) ? speedOnly : groundTruth;
        if (File.Exists(Path))
        {
            _entries.AddRange(GroundTruthFile.Read(Path));
        }
    }

    /// <summary>The file the annotations are read from and written to.</summary>
    public string Path { get; }

    public IReadOnlyList<GroundTruthEntry> Entries => _entries;

    /// <summary>Index of the selected (usually the last added) entry, -1 for none.</summary>
    public int SelectedIndex { get; set; } = -1;

    public GroundTruthEntry? Selected => SelectedIndex >= 0 && SelectedIndex < _entries.Count ? _entries[SelectedIndex] : null;

    /// <summary>Last save problem, or null.</summary>
    public string? Warning { get; private set; }

    /// <returns>True if the entry was added, false for a repeated key on the same crossing.</returns>
    public bool Add(GroundTruthEntry entry)
    {
        // Holding a key repeats it; the same crossing on the same frame is recorded once.
        if (_entries.Any(e => e.Offset == entry.Offset && e.Motion == entry.Motion))
        {
            return false;
        }

        _entries.Add(entry);
        SelectedIndex = _entries.Count - 1;
        Save();
        return true;
    }

    public void SetClass(VehicleClass vehicleClass) => ReplaceSelected(e => e with { Class = vehicleClass });

    public void SetNote(string? note) => ReplaceSelected(e => e with { Note = string.IsNullOrWhiteSpace(note) ? null : note });

    /// <summary>Speedometer reading of the selected entry; null removes it. Values outside (0, 200) are refused.</summary>
    /// <returns>False if no entry is selected or the value is out of range.</returns>
    public bool SetSpeed(float? kmh)
    {
        if (Selected is null || kmh is <= 0 or >= 200)
        {
            return false;
        }

        ReplaceSelected(e => e with { SpeedKmh = kmh });
        return true;
    }

    /// <summary>Removes the selected entry (key u).</summary>
    public void UndoSelected()
    {
        if (Selected is null)
        {
            return;
        }

        _entries.RemoveAt(SelectedIndex);
        SelectedIndex = -1;
        Save();
    }

    /// <summary>Deletes the entry closest to the position, if one lies within one second.</summary>
    public void DeleteNear(TimeSpan offset)
    {
        GroundTruthEntry? nearest = _entries.MinBy(e => (e.Offset - offset).Duration());
        if (nearest is null || (nearest.Offset - offset).Duration() > TimeSpan.FromSeconds(1))
        {
            return;
        }

        _entries.Remove(nearest);
        SelectedIndex = -1;
        Save();
    }

    /// <summary>
    /// Index of the next (or previous) bicycle entry. Searched from the selected entry if that lies ahead, so pressing
    /// the key again before playback reaches the selected bicycle moves on instead of finding it again.
    /// </summary>
    public int? FindBicycle(TimeSpan current, bool forward)
    {
        TimeSpan from = Selected is { } s && (forward ? s.Offset > current : s.Offset < current) ? s.Offset : current;
        List<int> bicycles = [.. Enumerable.Range(0, _entries.Count).Where(i => _entries[i].Class == VehicleClass.Bicycle)];
        // A video opens near its last entry; going forward from there starts over at the first bicycle.
        return forward
            ? bicycles.Where(i => _entries[i].Offset > from).OrderBy(i => _entries[i].Offset).Cast<int?>().FirstOrDefault()
                ?? bicycles.OrderBy(i => _entries[i].Offset).Cast<int?>().FirstOrDefault()
            : bicycles.Where(i => _entries[i].Offset < from).OrderByDescending(i => _entries[i].Offset).Cast<int?>().FirstOrDefault();
    }

    /// <summary>
    /// Index of the first entry after (or the last before) the position, ignoring entries within 0.1 s. With
    /// <paramref name="fromSelected"/> it steps from the selected entry instead: after a jump the shown frame can lie
    /// well before the entry (a gap in the recording), and searching from the frame found the same entry again.
    /// <paramref name="skipCars"/> passes over car entries, to review the rarer classes.
    /// </summary>
    public int? Neighbour(TimeSpan current, bool forward, bool fromSelected = false, bool skipCars = false)
    {
        List<int> order = [.. Enumerable.Range(0, _entries.Count).OrderBy(i => _entries[i].Offset).ThenBy(i => i)];
        IEnumerable<int> candidates;
        if (fromSelected && Selected is not null)
        {
            int at = order.IndexOf(SelectedIndex);
            candidates = forward ? order.Skip(at + 1) : order.Take(at).Reverse();
        }
        else
        {
            TimeSpan margin = TimeSpan.FromSeconds(0.1);
            candidates = forward
                ? order.Where(i => _entries[i].Offset > current + margin)
                : order.Where(i => _entries[i].Offset < current - margin).Reverse();
        }

        return candidates.Where(i => !skipCars || _entries[i].Class != VehicleClass.Car).Cast<int?>().FirstOrDefault();
    }

    /// <summary>Counts per class (in enum order, unknown last) and direction, only classes that occur.</summary>
    public IReadOnlyList<ClassCount> Counts() =>
    [
        .. _entries.GroupBy(e => e.Class)
            .OrderBy(g => g.Key is null or VehicleClass.Unknown ? int.MaxValue : (int)g.Key)
            .Select(g => new ClassCount(g.Key, g.Count(e => e.Motion == ImageMotion.RightToLeft), g.Count(e => e.Motion == ImageMotion.LeftToRight))),
    ];

    /// <summary>
    /// Timeline marks for <paramref name="buckets"/> equal stretches of the video. Several entries in one stretch
    /// show the class that is rarest in this video, so a single bicycle among cars stays visible.
    /// </summary>
    public IReadOnlyList<TimelineMark> Marks(TimeSpan duration, int buckets)
    {
        if (duration <= TimeSpan.Zero || buckets <= 0 || _entries.Count == 0)
        {
            return [];
        }

        Dictionary<VehicleClass, int> frequency = _entries.Where(e => e.Class is not null)
            .GroupBy(e => e.Class!.Value).ToDictionary(g => g.Key, g => g.Count());
        return
        [
            .. _entries.GroupBy(e => Math.Clamp((int)(e.Offset / duration * buckets), 0, buckets - 1))
                .OrderBy(g => g.Key)
                .Select(g => new TimelineMark(
                    g.Key,
                    g.Where(e => e.Class is not null and not VehicleClass.Unknown)
                        .Select(e => e.Class!.Value)
                        .OrderBy(c => frequency[c]).Cast<VehicleClass?>().FirstOrDefault() ?? g.First().Class,
                    g.Any(e => e.SpeedKmh is not null))),
        ];
    }

    private void ReplaceSelected(Func<GroundTruthEntry, GroundTruthEntry> change)
    {
        if (Selected is not { } entry)
        {
            return;
        }

        _entries[SelectedIndex] = change(entry);
        Save();
    }

    /// <summary>
    /// Writes the annotations after every change. A file briefly locked by a virus scanner or sync tool must not end
    /// the session: the entries stay in memory, a warning is shown and the next change writes them again. A video
    /// that was only opened gets no file, so the file list does not mark it as annotated.
    /// </summary>
    public void Save()
    {
        if (_entries.Count == 0 && !File.Exists(Path))
        {
            return;
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                GroundTruthFile.Write(Path, _entries);
                Warning = null;
                return;
            }
            catch (IOException ex) when (attempt < 5)
            {
                Warning = ex.Message;
                Thread.Sleep(100);
            }
            catch (IOException ex)
            {
                Warning = $"NOT SAVED: {ex.Message}";
                return;
            }
        }
    }
}
