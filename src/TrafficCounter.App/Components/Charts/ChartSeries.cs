namespace TrafficCounter.App.Components.Charts;

/// <summary>One stacked series of a <see cref="BarChart"/>.</summary>
public sealed record ChartSeries(string Name, string Color, IReadOnlyList<double> Values);

/// <summary>A line drawn over a <see cref="BarChart"/>; null values leave gaps.</summary>
public sealed record ChartLine(string Name, string Color, IReadOnlyList<double?> Values);

/// <summary>
/// A coloured strip between the bars and their labels, e.g. holidays: per bar the text of its tooltip, or null where
/// the strip is not drawn. <see cref="CssClass"/> gives the colour; strips with the same <see cref="Row"/> share a line.
/// </summary>
public sealed record ChartStrip(string CssClass, IReadOnlyList<string?> Titles, int Row = 0);
