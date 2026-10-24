namespace TrafficCounter.App;

/// <summary>
/// The date range last chosen on any page (date fields or quick buttons), kept for the browser session (one circuit),
/// so the next page opens with the same range instead of its own default.
/// </summary>
public sealed class SelectedRange
{
    private (DateTime From, DateTime To)? _range;

    /// <summary>The chosen range, or the page's default if nothing was chosen yet.</summary>
    public (DateTime From, DateTime To) Or(DateTime from, DateTime to) => _range ?? (from, to);

    public void Set(DateTime from, DateTime to) => _range = (from.Date, to.Date);
}
