namespace TrafficCounter.Core.Geometry;

/// <summary>Axis-aligned box in image pixel coordinates.</summary>
public readonly record struct BoundingBox(float Left, float Top, float Width, float Height)
{
    public float Right => Left + Width;

    public float Bottom => Top + Height;

    public float Area => Width * Height;

    public Point2 Center => new(Left + (Width / 2), Top + (Height / 2));

    /// <summary>Middle of the bottom edge – approximates where the object touches the road.</summary>
    public Point2 BottomCenter => new(Left + (Width / 2), Bottom);

    public static BoundingBox FromCorners(float left, float top, float right, float bottom) =>
        new(left, top, right - left, bottom - top);

    public float IntersectionOverUnion(BoundingBox other)
    {
        float width = MathF.Min(Right, other.Right) - MathF.Max(Left, other.Left);
        float height = MathF.Min(Bottom, other.Bottom) - MathF.Max(Top, other.Top);
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        float intersection = width * height;
        return intersection / (Area + other.Area - intersection);
    }
}
