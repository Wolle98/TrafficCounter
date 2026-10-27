using Microsoft.EntityFrameworkCore;

namespace TrafficCounter.Data;

/// <summary>Adding, changing and deleting notes on time spans (ADR-0051); used by the analysis app and the sync tool.</summary>
public static class DayNotes
{
    public const int MaxTextLength = 1000;

    /// <summary>Notes that are not deleted and overlap [<paramref name="from"/>, <paramref name="to"/>), by start.</summary>
    public static async Task<List<DayNote>> ListAsync(
        TrafficCounterDbContext db, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        [.. (await db.DayNotes.AsNoTracking()
            .Where(n => !n.Deleted && n.Start < to && n.End > from)
            .ToListAsync(cancellationToken))
            .OrderBy(n => n.Start)];

    public static async Task<DayNote> AddAsync(
        TrafficCounterDbContext db, DateTimeOffset start, DateTimeOffset end, string text, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        var note = new DayNote { Start = start, End = end, Text = Checked(start, end, text), CreatedAt = now, UpdatedAt = now };
        db.DayNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return note;
    }

    /// <returns>False if no such note exists (or it was deleted).</returns>
    public static async Task<bool> UpdateAsync(
        TrafficCounterDbContext db, Guid syncId, DateTimeOffset start, DateTimeOffset end, string text, CancellationToken cancellationToken = default)
    {
        DayNote? note = await db.DayNotes.SingleOrDefaultAsync(n => n.SyncId == syncId && !n.Deleted, cancellationToken);
        if (note is null)
        {
            return false;
        }

        note.Text = Checked(start, end, text);
        note.Start = start;
        note.End = end;
        note.UpdatedAt = DateTimeOffset.Now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Marks the note deleted; the sync passes that on to the other database.</summary>
    /// <returns>False if no such note exists (or it was deleted already).</returns>
    public static async Task<bool> DeleteAsync(TrafficCounterDbContext db, Guid syncId, CancellationToken cancellationToken = default)
    {
        DayNote? note = await db.DayNotes.SingleOrDefaultAsync(n => n.SyncId == syncId && !n.Deleted, cancellationToken);
        if (note is null)
        {
            return false;
        }

        note.Deleted = true;
        note.UpdatedAt = DateTimeOffset.Now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string Checked(DateTimeOffset start, DateTimeOffset end, string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > MaxTextLength)
        {
            throw new ArgumentException($"The text needs 1 to {MaxTextLength} characters.", nameof(text));
        }

        return end > start ? text : throw new ArgumentException("The end must lie after the start.", nameof(end));
    }
}
