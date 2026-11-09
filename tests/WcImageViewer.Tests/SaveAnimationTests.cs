using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class SaveAnimationTests
{
    [Fact]
    public void ThreeFrameGifSavedAsGifReadsBackThreeFrames()
    {
        StaRunner.Run(() =>
        {
            var frames = MakeFrames();
            var format = SaveFormatCatalog.ByFormat(SaveImageFormat.Gif);
            var path = Path.Combine(TestTemp.CreateFolder(), "anim.gif");
            ImageEncoderService.SaveFrames(frames, format, new ImageSaveOptions(), path);

            using var stream = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(
                stream,
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            Assert.Equal(3, decoder.Frames.Count);
        });
    }

    [Fact]
    public void ThreeFramesSavedAsWebPAnimationReadsBack()
    {
        StaRunner.Run(() =>
        {
            var frames = MakeFrames();
            var format = SaveFormatCatalog.ByFormat(SaveImageFormat.WebP);
            var options = new ImageSaveOptions { WebPLossless = true, WebPQuality = 90 };
            var path = Path.Combine(TestTemp.CreateFolder(), "anim.webp");
            ImageEncoderService.SaveFrames(frames, format, options, path);

            var data = File.ReadAllBytes(path);
            using var source = new WebPFrameSource(data, 1);
            Assert.Equal(3, source.FrameCount);

            using var stream = File.OpenRead(path);
            var delays = WebPAnimation.ReadFrameDelays(stream, source.FrameCount);
            Assert.Equal(3, delays.Length);
        });
    }

    private static IReadOnlyList<(BitmapSource Frame, int DelayMs)> MakeFrames()
    {
        return new[]
        {
            (MakeSolid(Colors.Red), 100),
            (MakeSolid(Colors.Lime), 100),
            (MakeSolid(Colors.Blue), 100),
        };
    }

    private static BitmapSource MakeSolid(Color color)
    {
        const int width = 16;
        const int height = 16;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }

        return BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
    }
}
