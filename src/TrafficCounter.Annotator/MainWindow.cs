using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using TrafficCounter.Core;
using TrafficCounter.Evaluation;

namespace TrafficCounter.Annotator;

/// <summary>
/// The annotator window (ADR-0043): video in the middle (scaled to the window, aspect ratio kept), file list and
/// counts on the left, class and direction buttons on the right, playback speed and image controls on top,
/// navigation, timeline and speed input below. Every key works as in the earlier key-only version; buttons call
/// the same actions.
/// </summary>
internal sealed class MainWindow : Window
{
    private static readonly double[] Speeds = [0.5, 1, 2, 4, 6, 8, 10, 12, 14, 16];
    private static readonly string[] VideoExtensions = [".mp4", ".avi", ".mkv", ".mov"];
    private static readonly TimeSpan QuitConfirmation = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReviewLeadIn = TimeSpan.FromSeconds(2);
    private const int DisplayWidth = 1280;

    private readonly double _linePosition;
    private readonly DispatcherTimer _timer = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _overlay = new() { Foreground = Brushes.White, Margin = new Thickness(6, 4), FontSize = 13 };
    private readonly TimelineControl _timeline = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBox _noteBox = new() { IsVisible = false };
    private readonly CheckBox _speedCheck = new() { Focusable = false };
    private readonly TextBox _speedBox = new() { Width = 70, PlaceholderText = "km/h" };
    private readonly StackPanel _speedRow = new() { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };
    private readonly Slider _brightness = new() { Minimum = -50, Maximum = 50, Value = 0, Width = 110, Focusable = false };
    private readonly Slider _contrast = new() { Minimum = 0.5, Maximum = 2.0, Value = 1, Width = 110, Focusable = false };
    private readonly TextBlock _brightnessValue = new() { Width = 34, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _contrastValue = new() { Width = 34, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<Button> _speedButtons = [];
    private readonly TextBlock _folderLabel = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly ListBox _files = new() { Focusable = false };
    private readonly Grid _counts = new();
    private readonly StackPanel _classOverlay = new() { Spacing = 4 };
    private readonly ComboBox _language = new() { Focusable = false, MinWidth = 110, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleSwitch _dark = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center, MinHeight = 0 };
    private readonly AnnotatorSettings _settings;

    /// <summary>Re-applies every localised text after a language change.</summary>
    private readonly List<Action> _texts = [];
    private string _classOverlayKey = "";

    private string _folder;
    private string? _videoPath;
    private VideoPlayer? _player;
    private AnnotationModel? _model;
    private WriteableBitmap? _bitmap;
    private bool _paused = true;
    private int _speedIndex = Array.IndexOf(Speeds, 1.0);
    private DateTime? _quitRequestedAt;
    private string? _message;

    public MainWindow(string? videoPath, string folder, double linePosition, bool speedMode, AnnotatorSettings settings)
    {
        _linePosition = linePosition;
        _settings = settings;
        _folder = folder;
        Title = "TrafficCounter annotator";
        Width = 1600;
        Height = 900;
        _speedCheck.IsChecked = speedMode;
        _speedRow.IsVisible = speedMode;
        _dark.IsChecked = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        Content = BuildLayout();

        _timer.Tick += (_, _) => PlayStep();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Closing += (_, _) => _model?.Save();
        Closed += (_, _) => _player?.Dispose();

        ShowFolder(folder);
        if (videoPath is not null)
        {
            OpenVideo(videoPath);
        }
        else
        {
            Refresh();
        }
    }

    private Control BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(8) };

        // Top: playback speed, brightness, contrast; language and light/dark on the right.
        var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var settingsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        _language.ItemsSource = Localizer.Languages.Select(l => l.Name).ToList();
        _language.SelectedIndex = Array.FindIndex(Localizer.Languages, l => l.Culture == Localizer.Culture.TwoLetterISOLanguageName);
        _language.SelectionChanged += (_, _) => ChangeLanguage();
        _dark.IsCheckedChanged += (_, _) => ChangeTheme();
        Localize(_dark, d => d.OnContent = d.OffContent = Localizer.T("Dark"));
        settingsPanel.Children.Add(_language);
        settingsPanel.Children.Add(_dark);
        DockPanel.SetDock(settingsPanel, Dock.Right);
        topBar.Children.Add(settingsPanel);
        var top = new WrapPanel();
        topBar.Children.Add(top);
        top.Children.Add(Label("Speed", new Thickness(0, 0, 6, 0)));
        for (int i = 0; i < Speeds.Length; i++)
        {
            int index = i;
            Button b = Btn($"{Speeds[i].ToString("0.#", CultureInfo.InvariantCulture)}x", () => SetSpeed(index));
            _speedButtons.Add(b);
            top.Children.Add(b);
        }

        top.Children.Add(new Border { Width = 24 });
        top.Children.Add(Label("Brightness", new Thickness(0, 0, 4, 0)));
        top.Children.Add(_brightness);
        top.Children.Add(_brightnessValue);
        top.Children.Add(Label("Contrast", new Thickness(8, 0, 4, 0)));
        top.Children.Add(_contrast);
        top.Children.Add(_contrastValue);
        top.Children.Add(Btn("Reset image", () =>
        {
            _brightness.Value = 0;
            _contrast.Value = 1;
        }));
        _brightness.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { ShowFrame(); } };
        _contrast.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { ShowFrame(); } };
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        // Left: files and counts.
        var left = new DockPanel { Width = 230, Margin = new Thickness(0, 0, 8, 0) };
        var folderButton = Btn("Folder…", async () => await PickFolderAsync());
        folderButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var leftTop = new StackPanel { Spacing = 4 };
        leftTop.Children.Add(Heading("Videos"));
        leftTop.Children.Add(folderButton);
        leftTop.Children.Add(_folderLabel);
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        var countsPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        countsPanel.Children.Add(Heading("Annotated in this video"));
        countsPanel.Children.Add(_counts);
        DockPanel.SetDock(countsPanel, Dock.Bottom);
        left.Children.Add(countsPanel);
        _files.SelectionChanged += (_, _) => OnFileSelected();
        left.Children.Add(_files);
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);

        // Right: class and direction quick buttons, undo, delete.
        var right = new StackPanel { Width = 190, Spacing = 4, Margin = new Thickness(8, 0, 0, 0) };
        right.Children.Add(Heading("Class and direction"));
        foreach (VehicleClass vehicleClass in ClassColors.QuickClasses)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*,34") };
            Button leftButton = Btn("←", () => AddEntry(ImageMotion.RightToLeft, vehicleClass, askSpeed: true));
            Button rightButton = Btn("→", () => AddEntry(ImageMotion.LeftToRight, vehicleClass, askSpeed: true));
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = ClassColors.BrushOf(vehicleClass), VerticalAlignment = VerticalAlignment.Center });
            name.Children.Add(Localize(new TextBlock { VerticalAlignment = VerticalAlignment.Center }, t => t.Text = ClassName(vehicleClass)));
            Grid.SetColumn(name, 1);
            Grid.SetColumn(rightButton, 2);
            leftButton.BorderBrush = rightButton.BorderBrush = ClassColors.BrushOf(vehicleClass);
            row.Children.Add(leftButton);
            row.Children.Add(name);
            row.Children.Add(rightButton);
            right.Children.Add(row);
        }

        right.Children.Add(new Border { Height = 8 });
        Button undo = Btn("Undo", Undo);
        Button delete = Btn("Delete", () => Do(m => m.DeleteNear(_player!.Offset)));
        undo.HorizontalAlignment = delete.HorizontalAlignment = HorizontalAlignment.Stretch;
        right.Children.Add(undo);
        right.Children.Add(delete);
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);

        // Bottom: timeline, navigation, speed input, status.
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        _timeline.Marks = buckets => _model is null || _player is null ? [] : _model.Marks(_player.Duration, buckets);
        _timeline.SeekRequested += share =>
        {
            if (_player is not null)
            {
                SeekTo((int)(share * (_player.FrameCount - 1)));
            }
        };
        bottom.Children.Add(_timeline);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        navigation.Children.Add(Btn("|◀ Previous", () => JumpToEntry(forward: false)));
        navigation.Children.Add(Btn("−5 s", () => SeekBy(-(int)(5 * Fps))));
        navigation.Children.Add(Btn("−1 s", () => SeekBy(-(int)Fps)));
        navigation.Children.Add(Btn("−1 frame", () => StepFrame(-1)));
        navigation.Children.Add(Btn("▶ / ❚❚", TogglePause));
        navigation.Children.Add(Btn("+1 frame", () => StepFrame(1)));
        navigation.Children.Add(Btn("+1 s", () => SeekBy((int)Fps)));
        navigation.Children.Add(Btn("+5 s", () => SeekBy((int)(5 * Fps))));
        navigation.Children.Add(Btn("Next ▶|", () => JumpToEntry(forward: true)));
        bottom.Children.Add(navigation);

        var speedLine = new DockPanel();
        _speedCheck.IsCheckedChanged += (_, _) =>
        {
            _speedRow.IsVisible = _speedCheck.IsChecked == true;
            Refresh();
        };
        _speedRow.Children.Add(_speedBox);
        _speedRow.Children.Add(Label("km/h on the speedometer", default));
        Localize(_speedCheck, c => c.Content = Localizer.T("Annotate speed"));
        Localize(_noteBox, n => n.PlaceholderText = Localizer.T("Note (Enter saves, Esc cancels)"));
        _speedRow.Children.Add(Btn("Save", SaveSpeed));
        _speedBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SaveSpeed();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Focus();
                e.Handled = true;
            }
        };
        var speedPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        speedPanel.Children.Add(_speedCheck);
        speedPanel.Children.Add(_speedRow);
        speedLine.Children.Add(speedPanel);
        bottom.Children.Add(speedLine);
        _noteBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Do(m => m.SetNote(_noteBox.Text));
                CloseNote();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseNote();
                e.Handled = true;
            }
        };
        bottom.Children.Add(_noteBox);
        bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        // Centre: the video with an information line on top.
        var video = new Grid { Background = Brushes.Black };
        video.Children.Add(_image);
        var overlayBack = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(3),
            Child = _overlay,
        };
        video.Children.Add(overlayBack);
        // The class labels sit on a canvas, so placing them never changes the layout of the video itself.
        var labels = new Canvas { IsHitTestVisible = false };
        labels.Children.Add(_classOverlay);
        video.Children.Add(labels);
        _classOverlay.SizeChanged += (_, _) => PlaceClassOverlay();
        _image.SizeChanged += (_, _) => PlaceClassOverlay();
        video.SizeChanged += (_, _) => PlaceClassOverlay();
        // Mouse wheel: one frame back (up) or forward (down).
        video.PointerWheelChanged += (_, e) =>
        {
            if (e.Delta.Y != 0)
            {
                StepFrame(e.Delta.Y > 0 ? -1 : 1);
                e.Handled = true;
            }
        };
        root.Children.Add(video);
        return root;
    }

    /// <summary>A button with a localised text (texts without translation, such as "+1 s", stay as they are).</summary>
    private Button Btn(string text, Action action)
    {
        var button = Localize(new Button { Focusable = false, Padding = new Thickness(8, 3), MinHeight = 28 }, b => b.Content = Localizer.T(text));
        button.Click += (_, _) => action();
        return button;
    }

    private TextBlock Label(string text, Thickness margin) =>
        Localize(new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = margin }, t => t.Text = Localizer.T(text));

    private TextBlock Heading(string text) => Localize(new TextBlock { FontWeight = FontWeight.Medium }, t => t.Text = Localizer.T(text));

    private T Localize<T>(T control, Action<T> apply)
    {
        apply(control);
        _texts.Add(() => apply(control));
        return control;
    }

    private static string ClassName(VehicleClass? vehicleClass) => Localizer.T(ClassColors.NameOf(vehicleClass));

    private void ChangeLanguage()
    {
        if (_language.SelectedIndex < 0)
        {
            return;
        }

        Localizer.Use(Localizer.Languages[_language.SelectedIndex].Culture);
        _settings.Language = Localizer.Culture.TwoLetterISOLanguageName;
        _settings.Save();
        foreach (Action apply in _texts)
        {
            apply();
        }

        _classOverlayKey = "";
        Refresh();
    }

    private void ChangeTheme()
    {
        bool dark = _dark.IsChecked == true;
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        _settings.Dark = dark;
        _settings.Save();
        _timeline.InvalidateVisual();
    }

    private double Fps => _player?.Fps ?? 25;

    // ---- Files ----

    private void ShowFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        _folder = Path.GetFullPath(folder);
        _folderLabel.Text = _folder;
        List<FileItem> items = [];
        if (Directory.GetParent(_folder) is { } parent)
        {
            items.Add(new FileItem("..", parent.FullName, IsFolder: true));
        }

        items.AddRange(Directory.GetDirectories(_folder).Order(StringComparer.OrdinalIgnoreCase)
            .Select(d => new FileItem("▸ " + Path.GetFileName(d), d, IsFolder: true)));
        items.AddRange(Directory.GetFiles(_folder).Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => new FileItem((File.Exists(GroundTruthFile.PathFor(f)) || File.Exists(GroundTruthFile.SpeedPathFor(f)) ? "✎ " : "   ") + Path.GetFileName(f), f, IsFolder: false)));
        _files.ItemsSource = items;
        _files.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Path, _videoPath, StringComparison.OrdinalIgnoreCase));
    }

    private void OnFileSelected()
    {
        if (_files.SelectedItem is not FileItem item)
        {
            return;
        }

        if (item.IsFolder)
        {
            Dispatcher.UIThread.Post(() => ShowFolder(item.Path));
        }
        else if (!string.Equals(item.Path, _videoPath, StringComparison.OrdinalIgnoreCase))
        {
            OpenVideo(item.Path);
        }
    }

    private async Task PickFolderAsync()
    {
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localizer.T("Folder with videos"),
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_folder),
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            ShowFolder(path);
        }
    }

    private void OpenVideo(string path)
    {
        _model?.Save();
        _timer.Stop();
        _paused = true;
        _player?.Dispose();
        _player = VideoPlayer.Open(path);
        if (_player is null)
        {
            _model = null;
            _videoPath = null;
            _message = Localizer.F("Video '{0}' could not be opened.", path);
            Refresh();
            return;
        }

        _videoPath = Path.GetFullPath(path);
        _model = new AnnotationModel(_videoPath);
        _message = _model.Entries.Count > 0 ? Localizer.F("Continuing {0} with {1} entries.", Path.GetFileName(_model.Path), _model.Entries.Count) : null;
        // Resume shortly before the last entry instead of at the start.
        int start = _model.Entries.Count > 0 ? _player.FrameAt(_model.Entries.Max(e => e.Offset) - TimeSpan.FromSeconds(5)) : 0;
        _player.Seek(start);
        string? folder = Path.GetDirectoryName(_videoPath);
        if (folder is not null && !string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase))
        {
            ShowFolder(folder);
        }

        ShowFrame();
        Refresh();
    }

    // ---- Playback ----

    private void SetSpeed(int index)
    {
        _speedIndex = Math.Clamp(index, 0, Speeds.Length - 1);
        UpdateTimer();
        Refresh();
    }

    private int Step => Math.Max(1, (int)Math.Round(Speeds[_speedIndex] / 2));

    private void UpdateTimer()
    {
        // Above 2x only every n-th frame is shown; the others are decoded but not drawn.
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, 1000 / Fps * Step / Speeds[_speedIndex]));
        if (_paused || _player is null)
        {
            _timer.Stop();
        }
        else
        {
            _timer.Start();
        }
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        UpdateTimer();
        Refresh();
    }

    private void TogglePause() => SetPaused(!_paused);

    private void PlayStep()
    {
        if (_player is null)
        {
            return;
        }

        if (!_player.Advance(Step))
        {
            SetPaused(true); // end of video
        }

        ShowFrame();
        RefreshOverlay();
    }

    private void SeekTo(int frame)
    {
        if (_player is null)
        {
            return;
        }

        _player.Seek(frame);
        ShowFrame();
        Refresh();
    }

    private void SeekBy(int frames) => SeekTo((_player?.Position ?? 0) + frames);

    private void StepFrame(int frames)
    {
        _paused = true;
        UpdateTimer();
        SeekBy(frames);
    }

    private void ShowFrame()
    {
        if (_player is null)
        {
            return;
        }

        using OpenCvSharp.Mat shown = _player.Render(DisplayWidth, _brightness.Value, _contrast.Value, _linePosition);
        if (shown.Empty())
        {
            return;
        }

        if (_bitmap is null || _bitmap.PixelSize.Width != shown.Width || _bitmap.PixelSize.Height != shown.Height)
        {
            _bitmap = new WriteableBitmap(new PixelSize(shown.Width, shown.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }

        using (ILockedFramebuffer target = _bitmap.Lock())
        {
            int rowBytes = shown.Width * 4;
            int height = shown.Height;
            byte[] row = new byte[rowBytes];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(shown.Ptr(y), row, 0, rowBytes);
                Marshal.Copy(row, 0, target.Address + (y * target.RowBytes), rowBytes);
            }
        }

        _image.Source = null;
        _image.Source = _bitmap;
        _brightnessValue.Text = _brightness.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        _contrastValue.Text = _contrast.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    // ---- Annotation actions ----

    private bool SpeedMode => _speedCheck.IsChecked == true;

    private void Do(Action<AnnotationModel> action)
    {
        if (_model is null || _player is null)
        {
            return;
        }

        action(_model);
        Refresh();
    }

    /// <summary>
    /// Marks a crossing at the current frame. With a class (Num4/Num6, quick buttons) the speed is asked for next in
    /// speed mode; a mark without class (arrow keys) pauses in speed mode so the class can be chosen.
    /// </summary>
    private void AddEntry(ImageMotion motion, VehicleClass? vehicleClass, bool askSpeed)
    {
        if (_model is null || _player is null)
        {
            return;
        }

        bool added = _model.Add(new GroundTruthEntry(_player.Offset, motion, vehicleClass));
        if (added && SpeedMode)
        {
            SetPaused(true);
            if (askSpeed)
            {
                AskSpeed();
            }
        }

        Refresh();
    }

    private void SetClass(VehicleClass vehicleClass)
    {
        if (_model?.Selected is null)
        {
            return;
        }

        _model.SetClass(vehicleClass);
        if (SpeedMode)
        {
            SetPaused(true);
            AskSpeed();
        }

        Refresh();
    }

    /// <summary>
    /// Puts the cursor into the speed box, prefilled only with the selected entry's own reading: carrying over the
    /// previous drive's reading made Enter give other traffic a speed by accident.
    /// </summary>
    private void AskSpeed()
    {
        _speedCheck.IsChecked = true;
        _speedBox.Text = _model?.Selected?.SpeedKmh?.ToString("0.#", CultureInfo.InvariantCulture) ?? "";
        _speedBox.Focus();
        _speedBox.SelectAll();
    }

    private void SaveSpeed()
    {
        if (_model?.Selected is null)
        {
            _message = Localizer.T("Select an entry first (mark a crossing or jump to an entry).");
        }
        else if (string.IsNullOrWhiteSpace(_speedBox.Text))
        {
            _model.SetSpeed(null);
            _message = Localizer.T("Speed removed.");
        }
        else if (float.TryParse(_speedBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float kmh) && _model.SetSpeed(kmh))
        {
            _message = Localizer.F("Speed {0} km/h saved.", kmh.ToString("0.#", Localizer.Culture));
        }
        else
        {
            _message = Localizer.T("Enter a speed between 1 and 199 km/h.");
        }

        Focus();
        Refresh();
    }

    private void Undo() => Do(m => m.UndoSelected());

    private void JumpToEntry(bool forward)
    {
        if (_model is null || _player is null || _model.Neighbour(_player.Offset, forward) is not int index)
        {
            return;
        }

        _model.SelectedIndex = index;
        SetPaused(true);
        SeekTo(_player.FrameAt(_model.Entries[index].Offset));
    }

    private void FindBicycle(bool forward)
    {
        if (_model is null || _player is null)
        {
            return;
        }

        // Review of older counts: jump to the next (previous) bicycle, select it and play it at normal speed,
        // so a key such as 8 turns it into an e-scooter.
        if (_model.FindBicycle(_player.Offset, forward) is int found)
        {
            _model.SelectedIndex = found;
            _speedIndex = Array.IndexOf(Speeds, 1.0);
            SeekTo(_player.FrameAt(_model.Entries[found].Offset - ReviewLeadIn));
            SetPaused(false);
        }
        else
        {
            _message = Localizer.T(forward ? "No bicycle entries." : "No earlier bicycle.");
            Refresh();
        }
    }

    private void StartNote()
    {
        if (_model?.Selected is not { } selected)
        {
            return;
        }

        SetPaused(true);
        _noteBox.Text = selected.Note ?? "";
        _noteBox.IsVisible = true;
        _noteBox.Focus();
    }

    private void CloseNote()
    {
        _noteBox.IsVisible = false;
        Focus();
        Refresh();
    }

    // ---- Keys (unchanged from the key-only version) ----

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox)
        {
            return; // typing a note or a speed
        }

        string? symbol = e.KeySymbol;
        bool quit = e.Key == Key.Escape || symbol == "q";
        if (!quit)
        {
            _quitRequestedAt = null;
        }

        e.Handled = true;
        switch (e.Key)
        {
            case Key.Left:
                AddEntry(ImageMotion.RightToLeft, null, askSpeed: false);
                return;
            case Key.Right:
                AddEntry(ImageMotion.LeftToRight, null, askSpeed: false);
                return;
            case Key.NumPad4:
                AddEntry(ImageMotion.RightToLeft, VehicleClass.Car, askSpeed: true);
                return;
            case Key.NumPad6:
                AddEntry(ImageMotion.LeftToRight, VehicleClass.Car, askSpeed: true);
                return;
            case Key.Delete:
                Do(m => m.DeleteNear(_player!.Offset));
                return;
            case Key.Space:
                TogglePause();
                return;
            case Key.Add:
                SetSpeed(_speedIndex + 1);
                return;
            case Key.Subtract:
                SetSpeed(_speedIndex - 1);
                return;
        }

        switch (symbol)
        {
            case "q" or "\u001b" when quit:
                RequestQuit();
                return;
            case [>= '0' and <= '8'] digit:
                SetClass((VehicleClass)(digit[0] - '0'));
                return;
            case "b":
                FindBicycle(forward: true);
                return;
            case "B":
                FindBicycle(forward: false);
                return;
            case "n":
                StartNote();
                return;
            case "v" when _model?.Selected is not null:
                SetPaused(true);
                AskSpeed();
                return;
            case "u":
                Undo();
                return;
            case ",":
                StepFrame(-1);
                return;
            case ".":
                StepFrame(1);
                return;
            case "j":
                SeekBy(-(int)(5 * Fps));
                return;
            case "l":
                SeekBy((int)(5 * Fps));
                return;
            case "J":
                SeekBy(-(int)(60 * Fps));
                return;
            case "L":
                SeekBy((int)(60 * Fps));
                return;
            case "-":
                SetSpeed(_speedIndex - 1);
                return;
            case "+":
                SetSpeed(_speedIndex + 1);
                return;
        }

        if (quit)
        {
            RequestQuit();
            return;
        }

        e.Handled = false;
    }

    /// <summary>A single accidental key press must not end a long session: press twice to quit.</summary>
    private void RequestQuit()
    {
        if (_quitRequestedAt is DateTime requested && DateTime.UtcNow - requested < QuitConfirmation)
        {
            Close();
            return;
        }

        _quitRequestedAt = DateTime.UtcNow;
        Refresh();
    }

    // ---- Display ----

    private void Refresh()
    {
        for (int i = 0; i < _speedButtons.Count; i++)
        {
            _speedButtons[i].Classes.Set("accent", i == _speedIndex);
        }

        RefreshOverlay();
        RefreshCounts();
        _timeline.InvalidateVisual();
        if (!_speedBox.IsFocused)
        {
            _speedBox.Text = _model?.Selected?.SpeedKmh?.ToString("0.#", CultureInfo.InvariantCulture) ?? "";
        }

        List<string> lines = [];
        if (_model?.Selected is { } selected)
        {
            lines.Add(Localizer.F("Selected: {0}", Describe(selected)));
        }

        if (_model is not null)
        {
            lines.Add(Localizer.F("{0} entries in {1}", _model.Entries.Count, Path.GetFileName(_model.Path)));
        }

        if (_quitRequestedAt is not null)
        {
            lines.Add(Localizer.T("Press q again to quit"));
        }

        if (_model?.Warning is { } warning)
        {
            lines.Add(warning);
        }

        if (_message is not null)
        {
            lines.Add(_message);
        }

        lines.Add(Localizer.T(SpeedMode
            ? "Keys: <-/-> mark, 0-8 class then speedometer, Num4/Num6 car + speedometer, v speedometer, u undo, Del delete here, n note, space play, , . frame, j l 5 s, J L 1 min, - + speed, q q quit"
            : "Keys: <-/-> count, Num4/Num6 car, 0-8 class, b B next/previous bicycle, v speedometer, u undo, Del delete here, n note, space play, , . frame, j l 5 s, J L 1 min, - + speed, q q quit"));
        _status.Text = string.Join(Environment.NewLine, lines);
    }

    private void RefreshOverlay()
    {
        if (_player is null)
        {
            _overlay.Text = Localizer.T("No video – choose one on the left");
            _timeline.Progress = 0;
            RefreshClassOverlay();
            return;
        }

        string clock = _player.WallClockOf(_player.Position) is DateTimeOffset t ? $"{t.LocalDateTime:HH:mm:ss}  ·  " : "";
        _overlay.Text = $"{clock}{_player.Offset:hh\\:mm\\:ss\\.ff}  ·  {Localizer.T("frame")} {_player.Position}/{_player.FrameCount}  ·  {(_paused ? Localizer.T("paused") : $"{Speeds[_speedIndex].ToString("0.#", CultureInfo.InvariantCulture)}x")}";
        _timeline.Progress = _player.FrameCount > 1 ? (double)_player.Position / (_player.FrameCount - 1) : 0;
        _timeline.InvalidateVisual();
        RefreshClassOverlay();
    }

    /// <summary>
    /// The entries within a second of the current frame, above the counting line in their class colour, so it is
    /// visible at once which vehicle has been annotated as what; the selected entry is outlined.
    /// </summary>
    private void RefreshClassOverlay()
    {
        TimeSpan window = TimeSpan.FromSeconds(1);
        List<(GroundTruthEntry Entry, bool Selected)> near = _model is null || _player is null
            ? []
            : [.. _model.Entries.Select((e, i) => (Entry: e, Selected: i == _model.SelectedIndex))
                .Where(x => (x.Entry.Offset - _player.Offset).Duration() <= window)
                .OrderBy(x => x.Entry.Offset)];
        string key = string.Join("|", near.Select(x => $"{x.Entry}{x.Selected}"));
        if (key == _classOverlayKey)
        {
            return;
        }

        _classOverlayKey = key;
        _classOverlay.Children.Clear();
        foreach ((GroundTruthEntry entry, bool selected) in near)
        {
            string text = $"{(entry.Motion == ImageMotion.LeftToRight ? "→" : "←")}  {ClassName(entry.Class)}" +
                (entry.SpeedKmh is float kmh ? $"  {kmh.ToString("0.#", Localizer.Culture)} km/h" : "");
            _classOverlay.Children.Add(new Border
            {
                Background = ClassColors.BrushOf(entry.Class),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(selected ? 2 : 0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeight.SemiBold },
            });
        }
    }

    /// <summary>Centres the class labels above the counting line, a little below the top edge of the shown image.</summary>
    private void PlaceClassOverlay()
    {
        Rect image = _image.Bounds;
        Canvas.SetLeft(_classOverlay, image.X + (_linePosition * image.Width) - (_classOverlay.Bounds.Width / 2));
        Canvas.SetTop(_classOverlay, image.Y + Math.Max(12, image.Height * 0.05));
    }

    private void RefreshCounts()
    {
        _counts.Children.Clear();
        _counts.RowDefinitions.Clear();
        _counts.ColumnDefinitions = new ColumnDefinitions("*,40,40");
        IReadOnlyList<ClassCount> counts = _model?.Counts() ?? [];
        int row = 0;
        void Cell(Control control, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            _counts.Children.Add(control);
        }

        void Add(string name, string a, string b, VehicleClass? colour, bool bold)
        {
            _counts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (colour is not null || name == ClassName(null))
            {
                label.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = ClassColors.BrushOf(colour), VerticalAlignment = VerticalAlignment.Center });
            }

            FontWeight weight = bold ? FontWeight.Medium : FontWeight.Normal;
            label.Children.Add(new TextBlock { Text = name, FontWeight = weight });
            Cell(label, 0);
            Cell(new TextBlock { Text = a, FontWeight = weight, TextAlignment = TextAlignment.Right }, 1);
            Cell(new TextBlock { Text = b, FontWeight = weight, TextAlignment = TextAlignment.Right }, 2);
            row++;
        }

        Add("", "←", "→", null, bold: false);
        foreach (ClassCount c in counts)
        {
            Add(ClassName(c.Class), c.RightToLeft.ToString(CultureInfo.InvariantCulture), c.LeftToRight.ToString(CultureInfo.InvariantCulture), c.Class, bold: false);
        }

        Add(Localizer.T("Total"), counts.Sum(c => c.RightToLeft).ToString(CultureInfo.InvariantCulture), counts.Sum(c => c.LeftToRight).ToString(CultureInfo.InvariantCulture), null, bold: true);
        IReadOnlyList<GroundTruthEntry> entries = _model?.Entries ?? [];
        Add(Localizer.T("Speeds"),
            entries.Count(e => e.SpeedKmh is not null && e.Motion == ImageMotion.RightToLeft).ToString(CultureInfo.InvariantCulture),
            entries.Count(e => e.SpeedKmh is not null && e.Motion == ImageMotion.LeftToRight).ToString(CultureInfo.InvariantCulture),
            null, bold: false);
    }

    private static string Describe(GroundTruthEntry e) =>
        $"{e.Offset:hh\\:mm\\:ss\\.f}  {(e.Motion == ImageMotion.LeftToRight ? "→" : "←")}  {ClassName(e.Class)}" +
        (e.SpeedKmh is float kmh ? $"  {kmh.ToString("0.#", Localizer.Culture)} km/h" : "") + (e.Note is { } note ? $"  [{note}]" : "");

    private sealed record FileItem(string Name, string Path, bool IsFolder)
    {
        public override string ToString() => Name;
    }
}
