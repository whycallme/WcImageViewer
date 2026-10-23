using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WcImageViewer.Controls;

namespace WcImageViewer.Tests;

public class ZoomPanControlTests
{
    [Fact]
    public void FitDoesNotUpscaleWhenDisabled()
    {
        var zoom = StaRunner.Run(() => Fit(allowUpscale: false));
        Assert.Equal(1.0, zoom, 3);
    }

    [Fact]
    public void FitUpscalesWhenAllowed()
    {
        var zoom = StaRunner.Run(() => Fit(allowUpscale: true));
        Assert.Equal(8.0, zoom, 3);
    }

    private static double Fit(bool allowUpscale)
    {
        var control = new ZoomPanControl
        {
            Width = 800,
            Height = 600,
            AllowUpscaleOnFit = allowUpscale
        };

        control.Measure(new Size(800, 600));
        control.Arrange(new Rect(0, 0, 800, 600));

        var pixels = new byte[100 * 50 * 4];
        var bitmap = BitmapSource.Create(
            100,
            50,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            100 * 4);
        bitmap.Freeze();

        control.SetImage(bitmap);
        return control.Zoom;
    }
}
