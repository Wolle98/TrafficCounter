using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Counting;

namespace TrafficCounter.Pipeline;

public sealed class RelativePoint
{
    public float X { get; set; }

    public float Y { get; set; }
}

/// <summary>Rectangle as fractions of the frame size.</summary>
public sealed class RelativeRect
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    public bool Contains(float x, float y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;

    public bool OverlapsRows(float top, float bottom) => Y < bottom && Y + Height > top;
}

/// <summary>
/// Site-specific geometry (private configuration, written by the calibrator). All positions are fractions of the
/// frame size, so they stay valid when the resolution changes.
/// </summary>
public sealed class SiteOptions
{
    public string CameraName { get; set; } = "street";

    /// <summary>
    /// Direction A = crossing onto the right-hand side of the line, looking from <see cref="LineStart"/> to
    /// <see cref="LineEnd"/>. For a line drawn from the far kerb (top) to the near kerb (bottom), A is right-to-left.
    /// </summary>
    public string DirectionAName { get; set; } = "A";

    public string DirectionBName { get; set; } = "B";

    /// <summary>Top of the evaluation band as fraction of the frame height (ADR-0020).</summary>
    public float BandTop { get; set; }

    public float BandBottom { get; set; } = 1;

    /// <summary>
    /// Left and right edge of the evaluation band as fractions of the frame width (ADR-0040); default the full
    /// width. Leaves out distorted or disturbed image edges, and a band closer to the model's aspect ratio makes
    /// every vehicle larger in the detector input.
    /// </summary>
    public float BandLeft { get; set; }

    public float BandRight { get; set; } = 1;

    public RelativePoint LineStart { get; set; } = new() { X = 0.5f, Y = 0.3f };

    public RelativePoint LineEnd { get; set; } = new() { X = 0.5f, Y = 0.9f };

    /// <summary>Hysteresis around the line as fraction of the frame width (≈ 10 px at 1920).</summary>
    public float HysteresisFraction { get; set; } = 0.005f;

    /// <summary>Distance beyond the line before a passage is emitted, fraction of the frame width (≈ 300 px at 1920).</summary>
    public float EmitDistanceFraction { get; set; } = 0.15f;

    public float SegmentMargin { get; set; } = 0.1f;

    public int MinTrackFrames { get; set; } = 3;

    /// <summary>
    /// Position of the far and near kerb along the counting line (0 = start, 1 = end). Passages with a
    /// <c>LinePosition</c> outside this range were on a sidewalk (ADR-0020); used by the evaluation.
    /// </summary>
    public float? FarKerb { get; set; }

    public float? NearKerb { get; set; }

    /// <summary>
    /// Speed scale (stage 5, ADR-0015): meters covered by the full image width at the far and at the near kerb.
    /// Null: no speeds. Estimated from car lengths with "Regression --calibrate-speed", later from measured points.
    /// </summary>
    public float? FarKerbMetersPerImageWidth { get; set; }

    public float? NearKerbMetersPerImageWidth { get; set; }

    /// <summary>
    /// Motorcycles slower than this on a sidewalk (outside the kerbs) are not counted: a motorcycle does not ride at
    /// walking pace on the sidewalk, but the detector takes prams, walkers and pushed bikes for one. Null: off.
    /// Needs the kerbs and the speed scale. Bicycles on the sidewalk still count (ADR-0020).
    /// </summary>
    public float? SidewalkMotorcycleMaxKmh { get; set; } = 10;

    /// <summary>
    /// E-scooters slower than this are not counted, on the sidewalk and on the road: pedestrians are now and then taken
    /// for an e-scooter, at 2–8 km/h, also when they cross the road at the counting line, while e-scooters ride at 12 km/h
    /// or more. A pushed e-scooter is not counted either. Needs the speed scale, not the kerbs. Null: off (ADR-0048,
    /// ADR-0050).
    /// </summary>
    public float? SlowEScooterMaxKmh { get; set; } = 8;

    /// <summary>
    /// True if the passage is a pram, a walker or a pedestrian taken for a motorcycle or an e-scooter (see
    /// <see cref="SidewalkMotorcycleMaxKmh"/>, <see cref="SlowEScooterMaxKmh"/>).
    /// </summary>
    public bool IsPseudoVehicle(VehicleClass vehicleClass, float? speedKmh, float linePosition) => vehicleClass switch
    {
        VehicleClass.Motorcycle => SidewalkMotorcycleMaxKmh is float limit && speedKmh is float speed && speed < limit
            && FarKerb is float far && NearKerb is float near
            && (linePosition < Math.Min(far, near) || linePosition > Math.Max(far, near)),
        VehicleClass.EScooter => SlowEScooterMaxKmh is float limit && speedKmh is float speed && speed < limit,
        _ => false,
    };

    /// <summary>Half width of the speed measuring zone around the counting line, as a fraction of the image width.</summary>
    public float SpeedZoneFraction { get; set; } = 0.2f;

    /// <summary>
    /// Shortest observation inside the speed zone that still gives a speed, in seconds. 0.4 s left fast cars on the near
    /// lane without a speed; 0.25 s still needs at least five positions (MinSamples), at 15 fps about 0.27 s.
    /// </summary>
    public float SpeedMinSeconds { get; set; } = 0.25f;

    /// <summary>
    /// Site coordinates for the sun position (day/dusk/night, ADR-0017). One decimal (about 10 km) is enough;
    /// the precision is the operator's choice. Location data – private configuration only.
    /// </summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>Static image regions (facades, poles, kerbs) for camera drift detection (ADR-0019).</summary>
    public List<RelativeRect> StaticZones { get; set; } = [];

    /// <summary>
    /// Areas off the road where vehicles are parked, e.g. driveways (ADR-0025). Detections whose ground point
    /// (bottom center of the box) lies inside are dropped before tracking. Must not touch the carriageway.
    /// </summary>
    public List<RelativeRect> IgnoreZones { get; set; } = [];

    /// <summary>Rows of the carriageway between the kerbs (fractions of the frame height), if the kerbs are set.</summary>
    public (float Top, float Bottom)? CarriagewayRows => FarKerb is float far && NearKerb is float near
        ? (Math.Min(RowAt(far), RowAt(near)), Math.Max(RowAt(far), RowAt(near)))
        : null;

    /// <summary>Rows spanned by the counting line: carriageway and sidewalks.</summary>
    public (float Top, float Bottom) LineRows => (Math.Min(LineStart.Y, LineEnd.Y), Math.Max(LineStart.Y, LineEnd.Y));

    /// <summary>True if a detection with this ground point (fractions of the frame) is to be ignored.</summary>
    public bool IsIgnored(float x, float y) => IgnoreZones.Any(z => z.Contains(x, y));

    private float RowAt(float linePosition) => LineStart.Y + (linePosition * (LineEnd.Y - LineStart.Y));

    public void Validate()
    {
        if (BandTop < 0 || BandBottom > 1 || BandTop >= BandBottom)
        {
            throw new InvalidOperationException($"Invalid evaluation band {BandTop}..{BandBottom}; expected 0 ≤ top < bottom ≤ 1.");
        }

        if (BandLeft < 0 || BandRight > 1 || BandLeft >= BandRight)
        {
            throw new InvalidOperationException($"Invalid evaluation band {BandLeft}..{BandRight} (left..right); expected 0 ≤ left < right ≤ 1.");
        }

        foreach (RelativePoint point in new[] { LineStart, LineEnd })
        {
            if (point.X is < 0 or > 1 || point.Y is < 0 or > 1)
            {
                throw new InvalidOperationException("Counting line points must lie within the frame (0..1).");
            }
        }

        if (LineStart.X == LineEnd.X && LineStart.Y == LineEnd.Y)
        {
            throw new InvalidOperationException("Counting line start and end must differ.");
        }

        if (CarriagewayRows is { } road && IgnoreZones.Any(z => z.OverlapsRows(road.Top, road.Bottom)))
        {
            throw new InvalidOperationException("Ignore zones must not overlap the carriageway between the kerbs.");
        }
    }

    /// <summary>
    /// Geometry for a recording that stores only the horizontal band <paramref name="top"/>..<paramref name="bottom"/>
    /// of the camera image (record --band): vertical positions are re-expressed relative to that band. A counting
    /// line reaching beyond the band is cut where it leaves it, keeping its direction, and the kerb positions along
    /// it are converted to the shorter line, so they still mark the same points of the road.
    /// </summary>
    public SiteOptions ForCrop(float top, float bottom)
    {
        float height = bottom - top;
        float Map(float y) => Math.Clamp((y - top) / height, 0, 1);
        var cropped = (SiteOptions)MemberwiseClone();
        cropped.BandTop = Map(BandTop);
        cropped.BandBottom = Map(BandBottom);

        // Part of the line inside the band, as parameters 0..1 along the original line.
        float t0 = 0, t1 = 1;
        float dy = LineEnd.Y - LineStart.Y;
        if (Math.Abs(dy) > 1e-6f)
        {
            float atTop = (top - LineStart.Y) / dy;
            float atBottom = (bottom - LineStart.Y) / dy;
            t0 = Math.Clamp(Math.Min(atTop, atBottom), 0, 1);
            t1 = Math.Clamp(Math.Max(atTop, atBottom), 0, 1);
        }

        RelativePoint At(float t) => new()
        {
            X = LineStart.X + (t * (LineEnd.X - LineStart.X)),
            Y = Map(LineStart.Y + (t * dy)),
        };
        float? Along(float? position) => position is float p && t1 > t0 ? (p - t0) / (t1 - t0) : position;
        cropped.LineStart = At(t0);
        cropped.LineEnd = At(t1);
        cropped.FarKerb = Along(FarKerb);
        cropped.NearKerb = Along(NearKerb);
        cropped.StaticZones = [];
        cropped.IgnoreZones = [.. IgnoreZones.Select(z =>
        {
            float zoneTop = Map(z.Y);
            return new RelativeRect { X = z.X, Y = zoneTop, Width = z.Width, Height = Map(z.Y + z.Height) - zoneTop };
        }).Where(z => z.Height > 0)];
        return cropped;
    }

    public Rect BandRect(Size frame)
    {
        int top = (int)Math.Round(BandTop * frame.Height);
        int bottom = (int)Math.Round(BandBottom * frame.Height);
        int left = (int)Math.Round(BandLeft * frame.Width);
        int right = (int)Math.Round(BandRight * frame.Width);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    /// <summary>
    /// Room the counting needs on each side of the line, as a fraction of the frame width: the distance a vehicle
    /// travels past the line before its passage is emitted, or the speed zone, whichever is larger, plus a margin
    /// for the tracker to pick a vehicle up before it reaches the line.
    /// </summary>
    public float RequiredRoomBesideLine => Math.Max(EmitDistanceFraction, SpeedZoneFraction) + 0.05f;

    /// <summary>
    /// Warning if the band leaves too little room beside the counting line (null if it is fine): vehicles would
    /// leave the band before their passage is complete, or before their speed can be measured.
    /// </summary>
    public string? BandRoomWarning()
    {
        float lineLeft = Math.Min(LineStart.X, LineEnd.X);
        float lineRight = Math.Max(LineStart.X, LineEnd.X);
        float room = Math.Min(lineLeft - BandLeft, BandRight - lineRight);
        return room < RequiredRoomBesideLine
            ? $"Only {room:P0} of the image width between the counting line and the band edge; at least {RequiredRoomBesideLine:P0} are needed."
            : null;
    }

    public LineSegment Line(Size frame) => new(
        new Point2(LineStart.X * frame.Width, LineStart.Y * frame.Height),
        new Point2(LineEnd.X * frame.Width, LineEnd.Y * frame.Height));

    public CountingSettings CountingSettings(Size frame) => new(
        Line(frame),
        HysteresisPixels: HysteresisFraction * frame.Width,
        SegmentMargin: SegmentMargin,
        EmitDistancePixels: EmitDistanceFraction * frame.Width,
        MinTrackFrames: MinTrackFrames,
        Speed: FarKerbMetersPerImageWidth is float far && NearKerbMetersPerImageWidth is float near
            && FarKerb is float farKerb && NearKerb is float nearKerb
            ? new SpeedSettings(far / frame.Width, near / frame.Width, farKerb, nearKerb, ZonePixels: SpeedZoneFraction * frame.Width, MinSeconds: SpeedMinSeconds)
            : null);
}
