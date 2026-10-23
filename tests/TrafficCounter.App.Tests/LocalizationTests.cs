using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace TrafficCounter.App.Tests;

public partial class LocalizationTests
{
    private static readonly ResourceManager Resources = new("TrafficCounter.App.Resources.Strings", typeof(Strings).Assembly);

    private static Dictionary<string, string> Texts(CultureInfo culture) =>
        Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex Placeholder();

    private static string[] Placeholders(string text) => [.. Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order()];

    [Fact]
    public void English_and_German_are_offered()
    {
        Assert.Contains(AppCultures.Supported, c => c.Name == "en");
        Assert.Contains(AppCultures.Supported, c => c.Name == "de");
        Assert.Equal("Deutsch", AppCultures.DisplayName(CultureInfo.GetCultureInfo("de")));
    }

    [Fact]
    public void Folders_without_a_translation_of_the_app_are_not_offered()
    {
        string directory = Directory.CreateTempSubdirectory("tc-cultures-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "fr"));
            File.WriteAllText(Path.Combine(directory, "fr", "Other.resources.dll"), "");
            Directory.CreateDirectory(Path.Combine(directory, "it"));
            File.WriteAllText(Path.Combine(directory, "it", typeof(Strings).Assembly.GetName().Name + ".resources.dll"), "");
            Directory.CreateDirectory(Path.Combine(directory, "wwwroot"));

            Assert.Equal(["en", "it"], AppCultures.Discover(directory).Select(c => c.Name));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Every_translation_has_all_texts_with_the_same_placeholders()
    {
        Dictionary<string, string> english = Texts(CultureInfo.InvariantCulture);
        Assert.NotEmpty(english);
        foreach (CultureInfo culture in AppCultures.Supported.Where(c => c.Name != AppCultures.Default))
        {
            Dictionary<string, string> translated = Texts(culture);
            Assert.Empty(english.Keys.Except(translated.Keys));
            Assert.Empty(translated.Keys.Except(english.Keys));
            foreach ((string key, string text) in english)
            {
                Assert.True(Placeholders(text).SequenceEqual(Placeholders(translated[key])), $"{culture.Name}: placeholders of {key} differ");
            }
        }
    }

    [Fact]
    public void Date_formats_are_valid_in_every_language()
    {
        var sample = new DateTime(2026, 10, 7, 17, 5, 9);
        foreach (CultureInfo culture in AppCultures.Supported)
        {
            CultureInfo neutral = culture.Name == AppCultures.Default ? CultureInfo.InvariantCulture : culture;
            foreach ((string key, string format) in Texts(neutral).Where(t => t.Key.StartsWith("Format.", StringComparison.Ordinal) && !t.Key.StartsWith("Format.Excel", StringComparison.Ordinal)))
            {
                string text = sample.ToString(format, culture);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{culture.Name} {key}");
                if (format.Contains("yyyy", StringComparison.Ordinal))
                {
                    Assert.Contains("2026", text, StringComparison.Ordinal);
                }
            }
        }
    }
}
