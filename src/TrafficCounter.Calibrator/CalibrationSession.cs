using OpenCvSharp;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Pipeline;
using TrafficCounter.Vision;

namespace TrafficCounter.Calibrator;

/// <summary>Interactive editing of the site geometry with mouse clicks on a still image.</summary>
/// <param name="modelAspect">Width/height of the detector input (e.g. 640x192 = 3.3), to show how well the band fits.</param>
internal sealed class CalibrationSession(Mat image, SiteOptions site, string outputPath, double? modelAspect = null)
{
    private const string WindowName = "TrafficCounter calibrator";
    private const int DisplayWidth = 1280;

    private enum Mode
    {
        None,
        Band,
        Sides,
        Line,
        Kerbs,
        Zone,
        Ignore,
    }

    private readonly List<Point2f> _clicks = [];
    private readonly double _scale = Math.Min(1.0, (double)DisplayWidth / image.Width);
    private MouseCallback? _callback; // keep the delegate alive while OpenCV holds it
    private Mode _mode = Mode.None;
    private bool _dirty;
    private string _status = "Choose a mode: b band, w band sides, l line, k kerbs, z zone, i ignore zone";

    public void Run()
    {
        Cv2.NamedWindow(WindowName, WindowFlags.AutoSize);
        _callback = OnMouse;
        Cv2.SetMouseCallback(WindowName, _callback);

        while (true)
        {
            using Mat display = Render();
            Cv2.ImShow(WindowName, display);
            int key = Cv2.WaitKey(30);
            if (key == -1)
            {
                if (Cv2.GetWindowProperty(WindowName, WindowPropertyFlags.Visible) < 1)
                {
                    break;
                }

                continue;
            }

            switch (key)
            {
                case 'b':
                    Start(Mode.Band, "Band: click the upper edge, then the lower edge (include vehicle roofs on the far lane)");
                    break;
                case 'w':
                    Start(Mode.Sides, "Band sides: click the left edge, then the right edge (leave room beside the line); W = full width");
                    break;
                case 'W':
                    site.BandLeft = 0;
                    site.BandRight = 1;
                    _dirty = true;
                    _status = "Band over the full width";
                    break;
                case 'l':
                    Start(Mode.Line, "Line: click the start at the far property line (top), then the end at the near one (bottom)");
                    break;
                case 'k':
                    Start(Mode.Kerbs, "Kerbs: click the far kerb, then the near kerb, close to the counting line");
                    break;
                case 'z':
                    Start(Mode.Zone, "Static zone: click two opposite corners of a facade, pole or kerb away from the road");
                    break;
                case 'i':
                    Start(Mode.Ignore, "Ignore zone: click two corners around where parked vehicles stand (wheels), off the road");
                    break;
                case 'u':
                    site.IgnoreZones.Clear();
                    _dirty = true;
                    _status = "Ignore zones cleared";
                    break;
                case 'x':
                    site.StaticZones.Clear();
                    _dirty = true;
                    _status = "Static zones cleared";
                    break;
                case 's':
                    site.Validate();
                    SiteConfigurationFile.WriteGeometry(outputPath, site);
                    SaveDriftReference();
                    _dirty = false;
                    Console.WriteLine(_status);
                    break;
                case 'q' or 27:
                    if (_dirty)
                    {
                        Console.WriteLine("Unsaved changes were discarded.");
                    }

                    Cv2.DestroyAllWindows();
                    return;
            }
        }

        Cv2.DestroyAllWindows();
    }

    /// <summary>
    /// Stores ORB features of the static zones as reference for the drift check (ADR-0019) – no image.
    /// Only meaningful if the calibration image comes from the counting camera itself.
    /// </summary>
    private void SaveDriftReference()
    {
        string path = DriftReference.PathFor(outputPath);
        if (site.StaticZones.Count == 0)
        {
            _status = $"Saved to {outputPath} (no static zones, no drift reference)";
            return;
        }

        Size size = image.Size();
        DriftReference reference = DriftMonitor.CreateReference(image, site.StaticZones.Select(z => ToRect(z, size)));
        reference.Save(path);
        _status = $"Saved to {outputPath}, drift reference with {reference.Points.Count} features";
    }

    /// <summary>Renews only the drift reference from the saved static zones (no window, geometry unchanged).</summary>
    public string RenewDriftReference()
    {
        SaveDriftReference();
        return _status;
    }

    private void Start(Mode mode, string status)
    {
        _mode = mode;
        _clicks.Clear();
        _status = status;
    }

    private void OnMouse(MouseEventTypes @event, int x, int y, MouseEventFlags flags, IntPtr userData)
    {
        if (@event != MouseEventTypes.LButtonDown || _mode == Mode.None)
        {
            return;
        }

        // Store clicks as fractions of the image size.
        _clicks.Add(new Point2f((float)(x / _scale / image.Width), (float)(y / _scale / image.Height)));
        if (_clicks.Count < 2)
        {
            return;
        }

        Point2f a = _clicks[0];
        Point2f b = _clicks[1];
        switch (_mode)
        {
            case Mode.Band:
                site.BandTop = Math.Min(a.Y, b.Y);
                site.BandBottom = Math.Max(a.Y, b.Y);
                break;
            case Mode.Sides:
                site.BandLeft = Math.Min(a.X, b.X);
                site.BandRight = Math.Max(a.X, b.X);
                break;
            case Mode.Line:
                site.LineStart = new RelativePoint { X = a.X, Y = a.Y };
                site.LineEnd = new RelativePoint { X = b.X, Y = b.Y };
                break;
            case Mode.Kerbs:
                LineSegment line = site.Line(new Size(1, 1));
                site.FarKerb = Math.Clamp(line.Project(new Point2(a.X, a.Y)), 0, 1);
                site.NearKerb = Math.Clamp(line.Project(new Point2(b.X, b.Y)), 0, 1);
                break;
            case Mode.Zone:
                site.StaticZones.Add(Rectangle(a, b));
                break;
            case Mode.Ignore:
                if (!TryAddIgnoreZone(Rectangle(a, b)))
                {
                    _mode = Mode.None;
                    _clicks.Clear();
                    return;
                }

                break;
        }

        _dirty = true;
        _status = _mode == Mode.Ignore && _status.StartsWith("Warning", StringComparison.Ordinal)
            ? _status
            : _mode is Mode.Sides or Mode.Line && site.BandRoomWarning() is { } warning
                ? $"Warning: {warning}"
                : $"{_mode} set – s to save, or choose another mode";
        _mode = Mode.None;
        _clicks.Clear();
    }

    private static RelativeRect Rectangle(Point2f a, Point2f b) => new()
    {
        X = Math.Min(a.X, b.X),
        Y = Math.Min(a.Y, b.Y),
        Width = Math.Abs(a.X - b.X),
        Height = Math.Abs(a.Y - b.Y),
    };

    /// <summary>Rejects zones on the carriageway, warns about zones reaching into the sidewalks (cyclists).</summary>
    private bool TryAddIgnoreZone(RelativeRect zone)
    {
        if (site.CarriagewayRows is { } road && zone.OverlapsRows(road.Top, road.Bottom))
        {
            _status = "Rejected: an ignore zone must not overlap the carriageway between the kerbs";
            return false;
        }

        site.IgnoreZones.Add(zone);
        (float top, float bottom) = site.LineRows;
        _status = zone.OverlapsRows(top, bottom)
            ? "Warning: the zone reaches into a sidewalk row - cyclists there would be ignored. s to save, u to clear"
            : _status;
        return true;
    }

    /// <summary>Writes the current geometry drawn onto the image, without interaction (for checking a configuration).</summary>
    public void RenderTo(string path)
    {
        _status = $"Configuration {Path.GetFileName(outputPath)}";
        using Mat display = Render();
        Cv2.ImWrite(path, display);
    }

    private Mat Render()
    {
        Mat display = new();
        Cv2.Resize(image, display, new Size(0, 0), _scale, _scale, InterpolationFlags.Area);
        Size size = display.Size();

        Cv2.Rectangle(display, site.BandRect(size), new Scalar(255, 200, 0), 2);
        foreach (RelativeRect zone in site.StaticZones)
        {
            Cv2.Rectangle(display, ToRect(zone, size), new Scalar(200, 0, 255), 2);
        }

        foreach (RelativeRect zone in site.IgnoreZones)
        {
            Rect rect = ToRect(zone, size);
            Cv2.Rectangle(display, rect, new Scalar(128, 128, 128), 2);
            Label(display, "ignore", rect.TopLeft + new Point(4, 20));
        }

        LineSegment line = site.Line(size);
        Cv2.Line(display, ToPoint(line.Start), ToPoint(line.End), new Scalar(0, 220, 255), 3);
        DrawDirectionHint(display, line);
        foreach ((float? position, string label) in new[] { (site.FarKerb, "far kerb"), (site.NearKerb, "near kerb") })
        {
            if (position is float p)
            {
                Point2 point = line.Start + ((line.End - line.Start) * p);
                Cv2.Line(display, ToPoint(point) + new Point(-15, 0), ToPoint(point) + new Point(15, 0), new Scalar(0, 255, 0), 3);
                Label(display, label, ToPoint(point) + new Point(20, 5));
            }
        }

        foreach (Point2f click in _clicks)
        {
            Cv2.Circle(display, new Point(click.X * size.Width, click.Y * size.Height), 6, Scalar.Red, -1);
        }

        Label(display, _status, new Point(10, 28));
        Label(display, BandInfo(), new Point(10, 54));
        Label(display, "b band  w sides  l line  k kerbs  z zone  x clear  i ignore  u clear  s save  q quit", new Point(10, size.Height - 12));
        return display;
    }

    /// <summary>Band size in image pixels and its aspect ratio next to the model's: the closer, the larger the vehicles in the detector input.</summary>
    private string BandInfo()
    {
        Rect band = site.BandRect(image.Size());
        double aspect = (double)band.Width / band.Height;
        string model = modelAspect is double m ? $", model {m:0.0}:1" : "";
        string room = site.BandRoomWarning() is null ? "" : "  - too little room beside the line";
        return $"band {band.Width}x{band.Height} = {aspect:0.0}:1{model}{room}";
    }

    /// <summary>Arrow towards the positive side of the line: vehicles moving this way are direction A.</summary>
    private void DrawDirectionHint(Mat display, LineSegment line)
    {
        Point2 direction = line.End - line.Start;
        var normal = new Point2(-direction.Y, direction.X) * (1 / direction.Length);
        Point2 middle = line.Start + (direction * 0.5f);
        Cv2.ArrowedLine(display, ToPoint(middle - (normal * 60)), ToPoint(middle + (normal * 60)), new Scalar(0, 220, 255), 2, tipLength: 0.25);
        SideLabel(display, $"A: {site.DirectionAName}", middle + (normal * 70), middle);
        SideLabel(display, $"B: {site.DirectionBName}", middle - (normal * 70), middle);
    }

    /// <summary>Label next to the line that grows away from it, so the two direction labels never overlap.</summary>
    private static void SideLabel(Mat display, string text, Point2 anchor, Point2 middle)
    {
        string ascii = Overlay.Ascii(text);
        int width = Cv2.GetTextSize(ascii, HersheyFonts.HersheySimplex, 0.6, 1, out _).Width;
        Label(display, ascii, ToPoint(anchor) + new Point(anchor.X < middle.X ? -width : 0, -10));
    }

    private static void Label(Mat image, string text, Point origin)
    {
        Cv2.PutText(image, Overlay.Ascii(text), origin + new Point(1, 1), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 3);
        Cv2.PutText(image, Overlay.Ascii(text), origin, HersheyFonts.HersheySimplex, 0.6, Scalar.White, 1);
    }

    private static Point ToPoint(Point2 point) => new((int)Math.Round(point.X), (int)Math.Round(point.Y));

    private static Rect ToRect(RelativeRect zone, Size size) =>
        new((int)(zone.X * size.Width), (int)(zone.Y * size.Height), (int)(zone.Width * size.Width), (int)(zone.Height * size.Height));
}
