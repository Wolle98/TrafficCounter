using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Tracking;

/// <summary>
/// Constant-velocity Kalman filter for a box (centre x/y, width, height), one decoupled 2-state filter per
/// dimension. Time is in seconds, so irregular frame intervals and dropped frames are handled correctly.
/// Noise levels scale with the box height, as in SORT/ByteTrack.
/// </summary>
internal sealed class KalmanBoxFilter
{
    private const double MeasurementStdPerHeight = 0.05;
    private const double PositionAccelerationStdPerHeight = 2.0; // box heights / s²
    private const double SizeAccelerationStdPerHeight = 0.5;
    private const double InitialPositionVelocityStdPerHeight = 10.0; // box heights / s
    private const double InitialSizeVelocityStdPerHeight = 1.0;

    // 0 = centre x, 1 = centre y, 2 = width, 3 = height
    private readonly Axis[] _axes = new Axis[4];

    public KalmanBoxFilter(BoundingBox box, Point2? velocity = null)
    {
        double h = Math.Max(1, box.Height);
        double measurementVariance = Square(MeasurementStdPerHeight * h);
        double positionVelocityVariance = velocity is null ? Square(InitialPositionVelocityStdPerHeight * h) : measurementVariance * 4;
        Point2 center = box.Center;
        _axes[0] = new Axis(center.X, velocity?.X ?? 0, 2 * measurementVariance, positionVelocityVariance);
        _axes[1] = new Axis(center.Y, velocity?.Y ?? 0, 2 * measurementVariance, positionVelocityVariance);
        _axes[2] = new Axis(box.Width, 0, 2 * measurementVariance, Square(InitialSizeVelocityStdPerHeight * h));
        _axes[3] = new Axis(box.Height, 0, 2 * measurementVariance, Square(InitialSizeVelocityStdPerHeight * h));
    }

    public BoundingBox Box
    {
        get
        {
            double width = Math.Max(1, _axes[2].X);
            double height = Math.Max(1, _axes[3].X);
            return new BoundingBox((float)(_axes[0].X - (width / 2)), (float)(_axes[1].X - (height / 2)), (float)width, (float)height);
        }
    }

    /// <summary>Velocity of the box centre in pixels per second.</summary>
    public Point2 Velocity => new((float)_axes[0].V, (float)_axes[1].V);

    public void Predict(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        double h = Math.Max(1, _axes[3].X);
        for (int i = 0; i < 4; i++)
        {
            double accelerationStd = (i < 2 ? PositionAccelerationStdPerHeight : SizeAccelerationStdPerHeight) * h;
            _axes[i].Predict(seconds, Square(accelerationStd));
        }
    }

    public void Update(BoundingBox box)
    {
        double measurementVariance = Square(MeasurementStdPerHeight * Math.Max(1, _axes[3].X));
        Point2 center = box.Center;
        _axes[0].Update(center.X, measurementVariance);
        _axes[1].Update(center.Y, measurementVariance);
        _axes[2].Update(box.Width, measurementVariance);
        _axes[3].Update(box.Height, measurementVariance);
    }

    private static double Square(double value) => value * value;

    /// <summary>Position/velocity state with 2x2 covariance [[P00, P01], [P01, P11]].</summary>
    private struct Axis(double x, double v, double positionVariance, double velocityVariance)
    {
        public double X = x;
        public double V = v;
        private double _p00 = positionVariance;
        private double _p01;
        private double _p11 = velocityVariance;

        public void Predict(double dt, double accelerationVariance)
        {
            X += V * dt;
            double dt2 = dt * dt;
            _p00 += (2 * dt * _p01) + (dt2 * _p11) + (accelerationVariance * dt2 * dt2 / 4);
            _p01 += (dt * _p11) + (accelerationVariance * dt2 * dt / 2);
            _p11 += accelerationVariance * dt2;
        }

        public void Update(double measurement, double measurementVariance)
        {
            double s = _p00 + measurementVariance;
            double k0 = _p00 / s;
            double k1 = _p01 / s;
            double residual = measurement - X;
            X += k0 * residual;
            V += k1 * residual;
            double p00 = _p00;
            double p01 = _p01;
            _p00 = (1 - k0) * p00;
            _p01 = (1 - k0) * p01;
            _p11 -= k1 * p01;
        }
    }
}
