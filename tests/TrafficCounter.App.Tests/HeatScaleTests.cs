using TrafficCounter.App.Data;

namespace TrafficCounter.App.Tests;

public class HeatScaleTests
{
    private static double Luminance(string hex) => HeatScale.Contrast(HeatScale.Parse(hex), (0, 0, 0));

    [Fact]
    public void Every_step_has_a_label_with_at_least_4_5_to_1_contrast()
    {
        for (int i = 0; i <= 100; i++)
        {
            (string background, string text) = HeatScale.For(i / 100.0);
            double contrast = HeatScale.Contrast(HeatScale.Parse(background), HeatScale.Parse(text));
            Assert.True(contrast >= 4.5, $"share {i} %: {text} on {background} has {contrast:0.0}:1");
        }
    }

    [Fact]
    public void Lightness_falls_monotonically_so_the_order_is_readable()
    {
        double previous = double.MaxValue;
        for (int i = 0; i <= 100; i++)
        {
            double luminance = Luminance(HeatScale.For(i / 100.0).Background);
            Assert.True(luminance <= previous + 1e-9, $"share {i} % is lighter than the step before");
            previous = luminance;
        }
    }
}
