using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Core.Tests;

public class GeometryTests
{
    [Fact]
    public void IoU_of_identical_boxes_is_one() =>
        Assert.Equal(1f, new BoundingBox(10, 10, 20, 20).IntersectionOverUnion(new BoundingBox(10, 10, 20, 20)), 5);

    [Fact]
    public void IoU_of_disjoint_boxes_is_zero() =>
        Assert.Equal(0f, new BoundingBox(0, 0, 10, 10).IntersectionOverUnion(new BoundingBox(20, 20, 10, 10)));

    [Fact]
    public void IoU_of_half_overlapping_boxes()
    {
        // Overlap 5x10 = 50, union 100 + 100 - 50 = 150.
        float iou = new BoundingBox(0, 0, 10, 10).IntersectionOverUnion(new BoundingBox(5, 0, 10, 10));
        Assert.Equal(1f / 3f, iou, 5);
    }

    [Fact]
    public void BottomCenter_is_middle_of_bottom_edge() =>
        Assert.Equal(new Point2(15, 40), new BoundingBox(10, 20, 10, 20).BottomCenter);

    [Theory]
    [InlineData(50, 10, 10)]   // below a left-to-right line (y downwards) = right-hand side
    [InlineData(50, -10, -10)] // above = left-hand side
    [InlineData(200, 0, 0)]    // on the infinite line, beyond the segment
    public void SignedDistance_is_positive_on_right_hand_side(float x, float y, float expected)
    {
        var line = new LineSegment(new Point2(0, 0), new Point2(100, 0));
        Assert.Equal(expected, line.SignedDistance(new Point2(x, y)), 5);
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(50, 0.5f)]
    [InlineData(100, 1f)]
    [InlineData(150, 1.5f)]
    public void Project_returns_position_along_segment(float x, float expected)
    {
        var line = new LineSegment(new Point2(0, 0), new Point2(100, 0));
        Assert.Equal(expected, line.Project(new Point2(x, 30)), 5);
    }
}
