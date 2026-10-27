using TrafficCounter.Core;
using OpenCvSharp;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Counting;

namespace TrafficCounter.Pipeline.Tests;

public class SiteOptionsTests
{
    [Fact]
    public void Relative_geometry_is_converted_to_pixels()
    {
        var site = new SiteOptions
        {
            BandTop = 0.32f,
            BandBottom = 0.78f,
            LineStart = new RelativePoint { X = 0.5f, Y = 0.44f },
            LineEnd = new RelativePoint { X = 0.5f, Y = 0.76f },
        };
        var frame = new Size(1920, 1080);

        Assert.Equal(new Rect(0, 346, 1920, 496), site.BandRect(frame));
        Assert.Equal(new LineSegment(new Point2(960, 475.2f), new Point2(960, 820.8f)), site.Line(frame));

        CountingSettings counting = site.CountingSettings(frame);
        Assert.Equal(9.6f, counting.HysteresisPixels, 3);
        Assert.Equal(288f, counting.EmitDistancePixels, 3);
    }

    [Fact]
    public void Band_sides_narrow_the_band_rectangle()
    {
        var site = new SiteOptions { BandTop = 0.3f, BandBottom = 0.7f, BandLeft = 0.125f, BandRight = 0.875f };

        Assert.Equal(new Rect(240, 324, 1440, 432), site.BandRect(new Size(1920, 1080)));
    }

    [Theory]
    [InlineData(0.6f, 0.4f)]
    [InlineData(-0.1f, 1f)]
    [InlineData(0f, 1.2f)]
    public void Invalid_band_sides_are_rejected(float left, float right) =>
        Assert.Throws<InvalidOperationException>(() => new SiteOptions { BandLeft = left, BandRight = right }.Validate());

    [Theory]
    [InlineData(VehicleClass.Motorcycle, 5f, 0.92f, true)]   // pram on the near sidewalk, taken for a motorcycle
    [InlineData(VehicleClass.Motorcycle, 5f, 0.05f, true)]   // the same on the far sidewalk
    [InlineData(VehicleClass.Motorcycle, 25f, 0.92f, false)] // too fast for a pram
    [InlineData(VehicleClass.Motorcycle, 5f, 0.40f, false)]  // on the road
    [InlineData(VehicleClass.Bicycle, 5f, 0.92f, false)]     // cyclists on the sidewalk count (ADR-0020)
    [InlineData(VehicleClass.Motorcycle, null, 0.92f, false)] // no speed: no decision
    [InlineData(VehicleClass.EScooter, 4.5f, 0.05f, true)]    // pedestrian on the far sidewalk taken for an e-scooter
    [InlineData(VehicleClass.EScooter, 9f, 0.92f, false)]     // slow, but faster than walking
    [InlineData(VehicleClass.EScooter, 5.4f, 0.40f, true)]    // pedestrian crossing the road at the line (ADR-0050)
    [InlineData(VehicleClass.EScooter, 14f, 0.40f, false)]    // e-scooter on the road
    [InlineData(VehicleClass.EScooter, null, 0.40f, false)]   // no speed: no decision
    public void Slow_motorcycles_on_the_sidewalk_and_slow_e_scooters_are_not_vehicles(VehicleClass vehicleClass, float? speed, float position, bool dropped)
    {
        var site = new SiteOptions { FarKerb = 0.15f, NearKerb = 0.67f };

        Assert.Equal(dropped, site.IsPseudoVehicle(vehicleClass, speed, position));
    }

    [Fact]
    public void The_motorcycle_rule_needs_the_kerbs_and_both_rules_can_be_switched_off()
    {
        Assert.False(new SiteOptions().IsPseudoVehicle(VehicleClass.Motorcycle, 5, 0.92f));
        Assert.False(new SiteOptions { FarKerb = 0.15f, NearKerb = 0.67f, SidewalkMotorcycleMaxKmh = null }
            .IsPseudoVehicle(VehicleClass.Motorcycle, 5, 0.92f));
        Assert.True(new SiteOptions().IsPseudoVehicle(VehicleClass.EScooter, 5, 0.40f));
        Assert.False(new SiteOptions { SlowEScooterMaxKmh = null }.IsPseudoVehicle(VehicleClass.EScooter, 5, 0.40f));
    }

    [Fact]
    public void Too_little_room_beside_the_line_is_warned_about()
    {
        // Default line in the middle; the passage is emitted 15 % of the width past the line, speeds need 20 %.
        Assert.Null(new SiteOptions { BandLeft = 0.2f, BandRight = 0.8f }.BandRoomWarning());
        Assert.NotNull(new SiteOptions { BandLeft = 0.35f, BandRight = 0.8f }.BandRoomWarning());
    }

    [Fact]
    public void A_slanted_line_cut_by_the_crop_keeps_its_direction_and_kerb_points()
    {
        // Line from (0.4, 0.2) to (0.6, 1.0), kerbs at 40 % and 80 % of it; the recording keeps rows 0.3..0.9.
        var site = new SiteOptions
        {
            LineStart = new RelativePoint { X = 0.4f, Y = 0.2f },
            LineEnd = new RelativePoint { X = 0.6f, Y = 1.0f },
            FarKerb = 0.4f,
            NearKerb = 0.8f,
        };

        SiteOptions cropped = site.ForCrop(0.3f, 0.9f);

        // The kerbs must still mark the same image points (x unchanged, y re-expressed in the crop).
        foreach ((float original, float now) in new[] { (site.FarKerb!.Value, cropped.FarKerb!.Value), (site.NearKerb!.Value, cropped.NearKerb!.Value) })
        {
            float x = site.LineStart.X + (original * (site.LineEnd.X - site.LineStart.X));
            float y = (site.LineStart.Y + (original * (site.LineEnd.Y - site.LineStart.Y)) - 0.3f) / 0.6f;
            Assert.Equal(x, cropped.LineStart.X + (now * (cropped.LineEnd.X - cropped.LineStart.X)), 4);
            Assert.Equal(y, cropped.LineStart.Y + (now * (cropped.LineEnd.Y - cropped.LineStart.Y)), 4);
        }

        // Same direction as before: the cut line is part of the original one.
        Assert.Equal(0.425f, cropped.LineStart.X, 4);
        Assert.Equal(0f, cropped.LineStart.Y, 4);
        Assert.Equal(0.575f, cropped.LineEnd.X, 4);
        Assert.Equal(1f, cropped.LineEnd.Y, 4);
    }

    [Fact]
    public void Geometry_is_translated_for_band_only_recordings()
    {
        // Recording stores rows 0.3..0.8 of the camera image; the same pixels must be analysed in both.
        var site = new SiteOptions
        {
            BandTop = 0.32f,
            BandBottom = 0.78f,
            LineStart = new RelativePoint { X = 0.5f, Y = 0.4f },
            LineEnd = new RelativePoint { X = 0.5f, Y = 0.8f },
        };

        SiteOptions cropped = site.ForCrop(0.3f, 0.8f);

        Assert.Equal(0.04f, cropped.BandTop, 4);
        Assert.Equal(0.96f, cropped.BandBottom, 4);
        Assert.Equal(0.2f, cropped.LineStart.Y, 4);
        Assert.Equal(1.0f, cropped.LineEnd.Y, 4);
        Assert.Equal(0.4f, site.LineStart.Y, 4); // original unchanged
    }

    [Theory]
    [InlineData(0.8f, 0.3f)]
    [InlineData(-0.1f, 0.5f)]
    [InlineData(0.2f, 1.2f)]
    public void Invalid_band_is_rejected(float top, float bottom) =>
        Assert.Throws<InvalidOperationException>(() => new SiteOptions { BandTop = top, BandBottom = bottom }.Validate());

    [Fact]
    public void Degenerate_line_is_rejected() =>
        Assert.Throws<InvalidOperationException>(() => new SiteOptions
        {
            LineStart = new RelativePoint { X = 0.5f, Y = 0.5f },
            LineEnd = new RelativePoint { X = 0.5f, Y = 0.5f },
        }.Validate());
}
