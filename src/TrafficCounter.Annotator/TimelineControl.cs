using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TrafficCounter.Annotator;

/// <summary>
/// Position in the video with one mark per annotated stretch, coloured by the rarest class there, and a small
/// triangle below where a speed is stored. A click jumps to that point.
/// </summary>
internal sealed class TimelineControl : Control
{
    private const double BarHeight = 14;
    private const double PixelsPerBucket = 3;

    /// <summary>Marks for a given number of stretches across the width.</summary>
    public Func<int, IReadOnlyList<TimelineMark>>? Marks { get; set; }

    /// <summary>Played share of the video, 0..1.</summary>
    public double Progress { get; set; }

    /// <summary>Raised with the clicked share of the video, 0..1.</summary>
    public event Action<double>? SeekRequested;

    public TimelineControl()
    {
        Height = BarHeight + 10;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        var bar = new Rect(0, 0, width, BarHeight);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), bar, 3);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(70, 55, 138, 221)), new Rect(0, 0, width * Math.Clamp(Progress, 0, 1), BarHeight), 3);

        int buckets = Math.Max(1, (int)(width / PixelsPerBucket));
        foreach (TimelineMark mark in Marks?.Invoke(buckets) ?? [])
        {
            double x = mark.Bucket * width / buckets;
            context.FillRectangle(ClassColors.BrushOf(mark.Class), new Rect(x, 0, Math.Max(2, PixelsPerBucket - 1), BarHeight));
            if (mark.HasSpeed)
            {
                var geometry = new StreamGeometry();
                using (StreamGeometryContext g = geometry.Open())
                {
                    g.BeginFigure(new Point(x + 1, BarHeight + 2), true);
                    g.LineTo(new Point(x - 3, BarHeight + 9));
                    g.LineTo(new Point(x + 5, BarHeight + 9));
                    g.EndFigure(true);
                }

                context.DrawGeometry(Brushes.Gray, null, geometry);
            }
        }

        double position = width * Math.Clamp(Progress, 0, 1);
        IBrush marker = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? Brushes.White : Brushes.Black;
        context.DrawLine(new Pen(marker, 2),new Point(position, 0), new Point(position, BarHeight));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bounds.Width > 0)
        {
            SeekRequested?.Invoke(Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1));
        }
    }
}
