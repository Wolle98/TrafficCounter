namespace TrafficCounter.App.Components.Charts;

/// <summary>One stacked series of a <see cref="BarChart"/>.</summary>
public sealed record ChartSeries(string Name, string Color, IReadOnlyList<double> Values);

/// <summary>A line drawn over a <see cref="BarChart"/>; null values leave gaps.</summary>
public sealed record ChartLine(string Name, string Color, IReadOnlyList<double?> Values);
