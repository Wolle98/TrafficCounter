using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace TrafficCounter.Annotator;

/// <summary>Avalonia application with the annotator window (ADR-0043).</summary>
internal sealed class App(string? videoPath, string folder, double linePosition, bool speedMode) : Application
{
    private readonly AnnotatorSettings _settings = AnnotatorSettings.Load();

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Localizer.Use(_settings.Language ?? Localizer.DefaultLanguage);
        RequestedThemeVariant = _settings.Dark switch
        {
            true => ThemeVariant.Dark,
            false => ThemeVariant.Light,
            null => ThemeVariant.Default,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(videoPath, folder, linePosition, speedMode, _settings);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
