using System.Text.Json;

namespace TrafficCounter.Annotator;

/// <summary>Language and light/dark layout, remembered per user in %APPDATA%\TrafficCounter\annotator.json.</summary>
internal sealed class AnnotatorSettings
{
    private static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrafficCounter", "annotator.json");

    public string? Language { get; set; }

    /// <summary>True dark, false light, null as Windows.</summary>
    public bool? Dark { get; set; }

    public static AnnotatorSettings Load()
    {
        try
        {
            return System.IO.File.Exists(File) ? JsonSerializer.Deserialize<AnnotatorSettings>(System.IO.File.ReadAllText(File)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A convenience only; the annotator works without it.
        }
    }
}
