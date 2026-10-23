using System.Globalization;
using System.Text;

namespace TrafficCounter.Vision;

/// <summary>Helpers for text drawn into images.</summary>
public static class Overlay
{
    private static readonly Dictionary<char, string> Replacements = new()
    {
        ['ä'] = "ae", ['ö'] = "oe", ['ü'] = "ue", ['Ä'] = "Ae", ['Ö'] = "Oe", ['Ü'] = "Ue", ['ß'] = "ss",
        ['≈'] = "~", ['→'] = "->", ['←'] = "<-", ['–'] = "-", ['—'] = "-", ['°'] = "deg", ['×'] = "x",
        ['„'] = "\"", ['“'] = "\"", ['”'] = "\"", ['‚'] = "'", ['‘'] = "'", ['’'] = "'", ['…'] = "...",
    };

    /// <summary>
    /// Makes text safe for <c>Cv2.PutText</c>: OpenCV's Hershey fonts only cover ASCII, and OpenCvSharp throws
    /// "Cannot marshal: Encountered unmappable character" for anything else (e.g. "≈" or umlauts in direction names).
    /// </summary>
    public static string Ascii(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c < 128)
            {
                result.Append(c);
            }
            else if (Replacements.TryGetValue(c, out string? replacement))
            {
                result.Append(replacement);
            }
            else
            {
                // Strip accents where possible (é → e), otherwise a placeholder.
                string decomposed = c.ToString().Normalize(NormalizationForm.FormD);
                char basic = decomposed.FirstOrDefault(d => d < 128 && CharUnicodeInfo.GetUnicodeCategory(d) != UnicodeCategory.NonSpacingMark);
                result.Append(basic != default ? basic : '?');
            }
        }

        return result.ToString();
    }
}
