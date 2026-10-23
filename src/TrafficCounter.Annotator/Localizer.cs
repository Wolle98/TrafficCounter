using System.Globalization;
using System.Resources;

namespace TrafficCounter.Annotator;

/// <summary>
/// UI texts of the annotator in the chosen language. The English text is the key (Resources/Strings.resx);
/// Strings.&lt;lang&gt;.resx holds the translations. Missing translations fall back to English.
/// </summary>
internal static class Localizer
{
    /// <summary>Languages offered in the window: culture name and the language's own name.</summary>
    public static readonly (string Culture, string Name)[] Languages = [("de", "Deutsch"), ("en", "English")];

    private static readonly ResourceManager Resources = new("TrafficCounter.Annotator.Resources.Strings", typeof(Localizer).Assembly);

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    /// <summary>The Windows display language if offered, else English.</summary>
    public static string DefaultLanguage =>
        Languages.Any(l => l.Culture == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : "en";

    public static void Use(string culture) =>
        Culture = CultureInfo.GetCultureInfo(Languages.Any(l => l.Culture == culture) ? culture : "en");

    public static string T(string english) => Resources.GetString(english, Culture) ?? english;

    public static string F(string english, params object[] args) => string.Format(Culture, T(english), args);
}
