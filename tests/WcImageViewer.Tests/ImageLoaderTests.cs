using System.IO;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class ImageLoaderTests
{
    [Fact]
    public void LoadsGeneratedPng()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wc_image_viewer_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "test.png");
            TestImages.CreatePng(path, 4, 2);

            var loaded = StaRunner.Run(() => ImageLoader.Load(path));

            Assert.Equal(4, loaded.PixelWidth);
            Assert.Equal(2, loaded.PixelHeight);
            Assert.True(loaded.IsFrozen);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void LoadsAllGifFrames()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "animated.gif");

        var document = StaRunner.Run(() => ImageLoader.LoadDocument(path));

        Assert.True(document.IsAnimated);
        Assert.Equal(3, document.Frames.Count);
        Assert.Equal(8, document.Image.PixelWidth);
        Assert.Equal(6, document.Image.PixelHeight);
        Assert.All(document.Frames, frame => Assert.True(frame.IsFrozen));
        Assert.Equal(TimeSpan.FromMilliseconds(120), document.Delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(250), document.Delays[1]);
    }

    [Fact]
    public void AppliesExifOrientation()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "rotated.jpg");

        var image = StaRunner.Run(() => ImageLoader.Load(path));

        // The JPEG is stored 8x6 with EXIF orientation 6 (rotate 90 CW).
        Assert.Equal(6, image.PixelWidth);
        Assert.Equal(8, image.PixelHeight);
    }

    [Fact]
    public void LoadsThumbnailOnWorkerThread()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wc_image_viewer_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "thumb.png");
            TestImages.CreatePng(path, 40, 20);

            var thumbnail = StaRunner.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return ImageLoader.LoadThumbnail(stream, 16);
            });

            Assert.True(thumbnail.PixelWidth > 0);
            Assert.True(thumbnail.IsFrozen);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
