using System.IO;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class WebPAnimationTests
{
    private static string TestPath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "animated.webp");

    [Fact]
    public void ReadsFrameDelaysFromAnmfChunks()
    {
        using var stream = File.OpenRead(TestPath);
        var delays = WebPAnimation.ReadFrameDelays(stream, 3);

        Assert.Equal(
            new[]
            {
                TimeSpan.FromMilliseconds(80),
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(120)
            },
            delays);
    }

    [Fact]
    public void MissingFramesUseTheDefaultDelay()
    {
        using var stream = File.OpenRead(TestPath);
        var delays = WebPAnimation.ReadFrameDelays(stream, 5);

        Assert.Equal(5, delays.Length);
        Assert.Equal(TimeSpan.FromMilliseconds(100), delays[3]);
        Assert.Equal(TimeSpan.FromMilliseconds(100), delays[4]);
    }

    [Fact]
    public void LoadsAnimatedWebPWhenTheOsCodecIsAvailable()
    {
        try
        {
            var document = StaRunner.Run(() => ImageLoader.LoadDocument(TestPath));

            Assert.True(document.IsAnimated);
            Assert.Equal(3, document.FrameCount);
            Assert.Equal(TimeSpan.FromMilliseconds(80), document.Delays[0]);
            Assert.Equal(TimeSpan.FromMilliseconds(40), document.Delays[1]);
            Assert.Equal(TimeSpan.FromMilliseconds(120), document.Delays[2]);
            Assert.Equal(32, document.GetFrame(1).PixelWidth);
        }
        catch (NotSupportedException)
        {
            // Windows without the WebP WIC extension cannot run this integration check.
        }
    }

    [Fact]
    public void LongAnimationsDecodeFramesOnDemand()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "long-animation.webp");
        ImageDocument document;

        try
        {
            document = StaRunner.Run(() => ImageLoader.LoadDocument(path));
        }
        catch (NotSupportedException)
        {
            // Windows without the WebP WIC extension cannot run this integration check.
            return;
        }

        try
        {
            Assert.True(document.IsAnimated);
            Assert.Equal(600, document.FrameCount);
            Assert.Single(document.Frames);
            Assert.Equal(600, document.Delays.Count);

            var first = document.GetFrame(0);
            var second = document.GetFrame(1);
            var last = document.GetFrame(599);

            Assert.Equal(16, first.PixelWidth);
            Assert.Equal(16, second.PixelWidth);
            Assert.Equal(16, last.PixelWidth);
            Assert.NotSame(first, second);
            Assert.NotSame(second, last);
        }
        finally
        {
            document.Dispose();
        }
    }
}
