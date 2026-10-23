using System.Globalization;
using Microsoft.Extensions.Localization;
using TrafficCounter.Core;

namespace TrafficCounter.App;

/// <summary>
/// Marker for the UI texts in <c>Resources/Strings.resx</c> (English, neutral) and <c>Resources/Strings.&lt;language&gt;.resx</c>.
/// A new language needs only a new file; <see cref="AppCultures"/> finds it at start-up.
/// </summary>
public sealed class Strings;

/// <summary>Languages offered by the app: English (the neutral resources) plus every compiled translation.</summary>
public static class AppCultures
{
    public const string Default = "en";

    public static IReadOnlyList<CultureInfo> Supported { get; } = Discover(AppContext.BaseDirectory);

    /// <summary>English plus each sub-folder of <paramref name="baseDirectory"/> holding this app's satellite assembly.</summary>
    public static IReadOnlyList<CultureInfo> Discover(string baseDirectory)
    {
        string satellite = typeof(Strings).Assembly.GetName().Name + ".resources.dll";
        List<CultureInfo> cultures = [CultureInfo.GetCultureInfo(Default)];
        foreach (string directory in Directory.Exists(baseDirectory) ? Directory.EnumerateDirectories(baseDirectory) : [])
        {
            if (!File.Exists(Path.Combine(directory, satellite)))
            {
                continue;
            }

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(Path.GetFileName(directory));
                if (!cultures.Contains(culture))
                {
                    cultures.Add(culture);
                }
            }
            catch (CultureNotFoundException)
            {
                // Not a culture folder.
            }
        }

        return [.. cultures.OrderBy(c => c.Name, StringComparer.Ordinal)];
    }

    /// <summary>Name of a language in that language, capitalised, e.g. "Deutsch", "English".</summary>
    public static string DisplayName(CultureInfo culture)
    {
        string name = culture.NativeName;
        return name.Length == 0 ? culture.Name : char.ToUpper(name[0], culture) + name[1..];
    }
}

/// <summary>Texts that depend on values rather than fixed keys.</summary>
public static class LocalizerExtensions
{
    public static string ClassName(this IStringLocalizer localizer, VehicleClass vehicleClass) => localizer[$"Class.{vehicleClass}"];

    /// <summary>Formats with the date format stored under <paramref name="formatKey"/> in the current culture.</summary>
    public static string Date(this IStringLocalizer localizer, DateTime value, string formatKey) =>
        value.ToString(localizer[formatKey], CultureInfo.CurrentCulture);

    /// <summary>Short weekday names starting on Monday, e.g. "Mo" … "So" or "Mon" … "Sun".</summary>
    public static IReadOnlyList<string> WeekdaysFromMonday() =>
        [.. Enumerable.Range(1, 7).Select(i => CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[i % 7].TrimEnd('.'))];
}
