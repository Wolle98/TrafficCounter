using System.Collections;
using System.Globalization;
using System.Resources;

namespace TrafficCounter.Annotator.Tests;

public sealed class LocalizerTests
{
    [Fact]
    public void Every_text_has_a_German_translation_with_the_same_placeholders()
    {
        var resources = new ResourceManager("TrafficCounter.Annotator.Resources.Strings", typeof(Localizer).Assembly);
        ResourceSet english = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;
        ResourceSet german = resources.GetResourceSet(CultureInfo.GetCultureInfo("de"), createIfNotExists: true, tryParents: false)!;

        foreach (DictionaryEntry entry in english)
        {
            string key = (string)entry.Key;
            string? translation = german.GetString(key);
            Assert.False(string.IsNullOrEmpty(translation), key);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(key.Contains($"{{{i}}}", StringComparison.Ordinal), translation.Contains($"{{{i}}}", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void An_unknown_language_falls_back_to_English_and_untranslated_texts_stay()
    {
        try
        {
            Localizer.Use("de");
            Assert.Equal("Pkw", Localizer.T("Car"));
            Assert.Equal("+1 s", Localizer.T("+1 s"));

            Localizer.Use("fr");
            Assert.Equal("en", Localizer.Culture.Name);
            Assert.Equal("Car", Localizer.T("Car"));
        }
        finally
        {
            Localizer.Use("en");
        }
    }
}
