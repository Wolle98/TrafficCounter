using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public class OverlayTests
{
    [Theory]
    [InlineData("exposure -7 ≈ 1/128 s", "exposure -7 ~ 1/128 s")]
    [InlineData("towards the town centre", "towards the town centre")]
    [InlineData("Straße nach Düsseldorf", "Strasse nach Duesseldorf")]
    [InlineData("Café – Zone 2°", "Cafe - Zone 2deg")]
    [InlineData("東", "?")]
    public void Text_is_made_ascii(string input, string expected) =>
        Assert.Equal(expected, Overlay.Ascii(input));

    [Fact]
    public void Converted_text_can_be_drawn()
    {
        // Without conversion OpenCvSharp throws "Cannot marshal: Encountered unmappable character".
        using var image = new Mat(60, 400, MatType.CV_8UC3, Scalar.Black);
        Cv2.PutText(image, Overlay.Ascii("≈ Straße → Zentrum"), new Point(5, 40), HersheyFonts.HersheySimplex, 0.6, Scalar.White);
        Assert.True(Cv2.CountNonZero(image.CvtColor(ColorConversionCodes.BGR2GRAY)) > 0);
    }
}
