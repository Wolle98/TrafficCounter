namespace TrafficCounter.Counting;

/// <summary>
/// Calibrated camera with lens distortion and a road plane (ADR-0056): turns image points into positions on the road
/// and back. Image coordinates are pixels of a reference frame (the full camera image the model was fitted in); the
/// processed frame is mapped onto it by <see cref="ForFrame"/>, a recording that holds only a band of rows by
/// <see cref="ForCrop"/>. World coordinates are meters in a local east/north/up frame.
/// </summary>
/// <param name="ReferenceWidth">Width of the reference frame in pixels.</param>
/// <param name="ReferenceHeight">Height of the reference frame in pixels.</param>
/// <param name="Focal">Focal length in reference pixels.</param>
/// <param name="PrincipalX">Principal point, x in reference pixels.</param>
/// <param name="PrincipalY">Principal point, y in reference pixels.</param>
/// <param name="K1">
/// Division model of the distortion: undistorted = distorted / (1 + K1 r²), with r in units of
/// <paramref name="DistortionScale"/> pixels from the principal point.
/// </param>
/// <param name="DistortionScale">Pixels per unit radius of the distortion model.</param>
/// <param name="Rotation">Rotation vector (Rodrigues) from world to camera axes.</param>
/// <param name="Center">Camera position in world coordinates.</param>
/// <param name="Ground">Road plane z = Ground[0] + Ground[1]·x + Ground[2]·y.</param>
/// <param name="RoadOrigin">Point on the road (x, y) from which along and across are measured (the far kerb at the line).</param>
/// <param name="RoadAxis">Unit vector (x, y) along the road.</param>
/// <param name="AcrossAxis">Unit vector (x, y) across the road, from the far to the near kerb.</param>
public sealed record CameraModel(
    double ReferenceWidth,
    double ReferenceHeight,
    double Focal,
    double PrincipalX,
    double PrincipalY,
    double K1,
    double DistortionScale,
    IReadOnlyList<double> Rotation,
    IReadOnlyList<double> Center,
    IReadOnlyList<double> Ground,
    IReadOnlyList<double> RoadOrigin,
    IReadOnlyList<double> RoadAxis,
    IReadOnlyList<double> AcrossAxis)
{
    private double[]? _matrix;

    /// <summary>Top of the processed rows in the reference frame, as a fraction of its height.</summary>
    public double CropTop { get; init; }

    /// <summary>Height of the processed rows in the reference frame, as a fraction of its height.</summary>
    public double CropHeight { get; init; } = 1;

    /// <summary>Width of the processed frame in pixels (set by <see cref="ForFrame"/>).</summary>
    public double FrameWidth { get; init; }

    /// <summary>Height of the processed frame in pixels (set by <see cref="ForFrame"/>).</summary>
    public double FrameHeight { get; init; }

    /// <summary>The model for frames of this size.</summary>
    public CameraModel ForFrame(double width, double height) => this with { FrameWidth = width, FrameHeight = height };

    /// <summary>The model for frames that hold only the rows from <paramref name="top"/> to <paramref name="bottom"/> (fractions).</summary>
    public CameraModel ForCrop(double top, double bottom) =>
        this with { CropTop = CropTop + (top * CropHeight), CropHeight = (bottom - top) * CropHeight };

    /// <summary>Principal point's x in frame pixels: left of it a box's left edge is the vehicle's camera-near edge.</summary>
    public double PrincipalFrameX => PrincipalX * FrameWidth / ReferenceWidth;

    /// <summary>Position on the road (meters along and across) of a frame point on the road surface.</summary>
    public (double Along, double Across) ToRoad(double x, double y)
    {
        (double wx, double wy, double _) = OnGround(Ray(x, y));
        return Road(wx, wy);
    }

    /// <summary>
    /// Position along the road of a vertical vehicle edge on the near side (<paramref name="across"/>) whose outermost
    /// image column is <paramref name="edgeX"/>. The edge rises from the road to the roof; the box top
    /// <paramref name="topY"/> is the roof's far edge, <paramref name="width"/> meters further across. Camera tilt and
    /// distortion make the outermost column differ from the one at the road, for tall vehicles by meters.
    /// <paramref name="outermostRight"/>: the edge is a box's right edge (else its left). Null if no position in
    /// ±50 m fits.
    /// </summary>
    public double? AlongOfEdge(double edgeX, double across, double topY, bool outermostRight, double width = 0)
    {
        double lo = -50, hi = 50;
        double fLo = EdgeColumn(lo, across, topY, outermostRight, width) - edgeX;
        double fHi = EdgeColumn(hi, across, topY, outermostRight, width) - edgeX;
        if (double.IsNaN(fLo) || double.IsNaN(fHi) || Math.Sign(fLo) == Math.Sign(fHi))
        {
            return null;
        }

        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            double f = EdgeColumn(mid, across, topY, outermostRight, width) - edgeX;
            if (double.IsNaN(f))
            {
                return null;
            }

            if (Math.Sign(f) == Math.Sign(fLo))
            {
                (lo, fLo) = (mid, f);
            }
            else
            {
                hi = mid;
            }
        }

        return (lo + hi) / 2;
    }

    /// <summary>Frame pixel of a world point, or null behind the camera.</summary>
    public (double X, double Y)? Project(double wx, double wy, double wz)
    {
        double[] r = Matrix;
        double dx = wx - Center[0], dy = wy - Center[1], dz = wz - Center[2];
        double cx = (r[0] * dx) + (r[1] * dy) + (r[2] * dz);
        double cy = (r[3] * dx) + (r[4] * dy) + (r[5] * dz);
        double cz = (r[6] * dx) + (r[7] * dy) + (r[8] * dz);
        if (cz <= 1e-6)
        {
            return null;
        }

        double ux = cx / cz * Focal / DistortionScale;
        double uy = cy / cz * Focal / DistortionScale;
        double ru = Math.Sqrt((ux * ux) + (uy * uy));
        double factor = 1;
        if (ru > 1e-12 && Math.Abs(K1) > 1e-12)
        {
            // Inverse of the division model: ru = rd / (1 + K1 rd²) solved for rd.
            double discriminant = 1 - (4 * K1 * ru * ru);
            if (discriminant < 0)
            {
                return null;
            }

            factor = (1 - Math.Sqrt(discriminant)) / (2 * K1 * ru) / ru;
        }

        double refX = (ux * factor * DistortionScale) + PrincipalX;
        double refY = (uy * factor * DistortionScale) + PrincipalY;
        return (refX * FrameWidth / ReferenceWidth, ((refY / ReferenceHeight) - CropTop) / CropHeight * FrameHeight);
    }

    /// <summary>World point on the road at a position along and across.</summary>
    public (double X, double Y, double Z) OnRoad(double along, double across)
    {
        double x = RoadOrigin[0] + (along * RoadAxis[0]) + (across * AcrossAxis[0]);
        double y = RoadOrigin[1] + (along * RoadAxis[1]) + (across * AcrossAxis[1]);
        return (x, y, Ground[0] + (Ground[1] * x) + (Ground[2] * y));
    }

    private (double Along, double Across) Road(double wx, double wy)
    {
        double dx = wx - RoadOrigin[0], dy = wy - RoadOrigin[1];
        return ((dx * RoadAxis[0]) + (dy * RoadAxis[1]), (dx * AcrossAxis[0]) + (dy * AcrossAxis[1]));
    }

    /// <summary>
    /// Outermost frame column of the vertical edge at this road position, up to the roof height whose far edge
    /// (<paramref name="width"/> further across) shows in row <paramref name="topY"/>.
    /// </summary>
    private double EdgeColumn(double along, double across, double topY, bool outermostRight, double width)
    {
        (double x, double y, double z) = OnRoad(along, across);
        (double farX, double farY, double farZ) = OnRoad(along, across - width);
        double low = 0, high = 6;
        for (int i = 0; i < 24; i++)
        {
            double mid = (low + high) / 2;
            if (Project(farX, farY, farZ + mid) is not { } p)
            {
                return double.NaN;
            }

            // Image rows grow downwards: while the projected top is below the box top, the edge is taller.
            if (p.Y > topY)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        const int Steps = 8;
        double column = outermostRight ? double.MinValue : double.MaxValue;
        for (int i = 0; i <= Steps; i++)
        {
            if (Project(x, y, z + (low * i / Steps)) is not { } p)
            {
                return double.NaN;
            }

            column = outermostRight ? Math.Max(column, p.X) : Math.Min(column, p.X);
        }

        return column;
    }

    private (double X, double Y, double Z) Ray(double x, double y)
    {
        double refX = x * ReferenceWidth / FrameWidth;
        double refY = (CropTop + (y / FrameHeight * CropHeight)) * ReferenceHeight;
        double nx = (refX - PrincipalX) / DistortionScale;
        double ny = (refY - PrincipalY) / DistortionScale;
        double s = 1 / (1 + (K1 * ((nx * nx) + (ny * ny))));
        double ux = nx * s * DistortionScale / Focal;
        double uy = ny * s * DistortionScale / Focal;

        // Camera to world: the transposed rotation.
        double[] r = Matrix;
        return ((r[0] * ux) + (r[3] * uy) + r[6], (r[1] * ux) + (r[4] * uy) + r[7], (r[2] * ux) + (r[5] * uy) + r[8]);
    }

    private (double X, double Y, double Z) OnGround((double X, double Y, double Z) ray)
    {
        // Center + t·ray on z = g0 + g1·x + g2·y.
        double denominator = ray.Z - (Ground[1] * ray.X) - (Ground[2] * ray.Y);
        double t = (Ground[0] + (Ground[1] * Center[0]) + (Ground[2] * Center[1]) - Center[2]) / denominator;
        return (Center[0] + (t * ray.X), Center[1] + (t * ray.Y), Center[2] + (t * ray.Z));
    }

    private double[] Matrix => _matrix ??= RotationMatrix(Rotation[0], Rotation[1], Rotation[2]);

    /// <summary>Row-major rotation matrix of a rotation vector (Rodrigues' formula).</summary>
    private static double[] RotationMatrix(double rx, double ry, double rz)
    {
        double theta = Math.Sqrt((rx * rx) + (ry * ry) + (rz * rz));
        if (theta < 1e-12)
        {
            return [1, 0, 0, 0, 1, 0, 0, 0, 1];
        }

        double kx = rx / theta, ky = ry / theta, kz = rz / theta;
        double c = Math.Cos(theta), s = Math.Sin(theta), v = 1 - c;
        return
        [
            c + (kx * kx * v), (kx * ky * v) - (kz * s), (kx * kz * v) + (ky * s),
            (ky * kx * v) + (kz * s), c + (ky * ky * v), (ky * kz * v) - (kx * s),
            (kz * kx * v) - (ky * s), (kz * ky * v) + (kx * s), c + (kz * kz * v),
        ];
    }
}
