using System.IO;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class GifCompositingTests
{
    [Fact]
    public void CompositesTransparentFramesOverThePreviousCanvas()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "gif-composite.gif");
        var document = StaRunner.Run(() => ImageLoader.LoadDocument(path));

        try
        {
            Assert.True(document.IsAnimated);
            Assert.Equal(3, document.FrameCount);

            var frame = document.GetFrame(1);
            var stride = frame.PixelWidth * 4;
            var pixels = TestImages.GetPixels(frame);

            // The frame only paints a green square; the rest must remain red from frame 0.
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[..4]);

            var center = 16 * stride + 16 * 4;
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels[center..(center + 4)]);
        }
        finally
        {
            document.Dispose();
        }
    }
}
