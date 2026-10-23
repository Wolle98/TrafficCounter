namespace TrafficCounter.Core.Geometry;

/// <summary>A point or vector in image pixel coordinates (x to the right, y downwards).</summary>
public readonly record struct Point2(float X, float Y)
{
    public float Length => MathF.Sqrt((X * X) + (Y * Y));

    public float Dot(Point2 other) => (X * other.X) + (Y * other.Y);

    /// <summary>Z component of the 3D cross product.</summary>
    public float Cross(Point2 other) => (X * other.Y) - (Y * other.X);

    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Point2 operator *(Point2 a, float factor) => new(a.X * factor, a.Y * factor);
}
