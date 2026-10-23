namespace TrafficCounter.Core.Geometry;

/// <summary>A directed line segment from <see cref="Start"/> to <see cref="End"/>.</summary>
public readonly record struct LineSegment(Point2 Start, Point2 End)
{
    public float Length => (End - Start).Length;

    /// <summary>
    /// Perpendicular distance of <paramref name="point"/> to the infinite line.
    /// Positive on the right-hand side when looking from Start to End in image coordinates (y downwards).
    /// </summary>
    public float SignedDistance(Point2 point) => (End - Start).Cross(point - Start) / Length;

    /// <summary>
    /// Position of the perpendicular projection of <paramref name="point"/> along the segment:
    /// 0 at Start, 1 at End, outside [0, 1] beyond the segment ends.
    /// </summary>
    public float Project(Point2 point)
    {
        Point2 direction = End - Start;
        return (point - Start).Dot(direction) / direction.Dot(direction);
    }
}
