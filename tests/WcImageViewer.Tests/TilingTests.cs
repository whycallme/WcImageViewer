using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class TilingTests
{
    [Theory]
    [InlineData(12000, 9000, true)]
    [InlineData(10001, 10000, true)]
    [InlineData(10000, 10000, false)]
    [InlineData(16000, 7000, true)]
    [InlineData(8000, 6000, false)]
    public void TilesOnlyWhenImageIsVeryLarge(int width, int height, bool expected)
    {
        Assert.Equal(expected, ImageLoader.ShouldUseTiles(width, height));
    }

    [Fact]
    public void LowerZoomLevelsUseLargerSourceTiles()
    {
        Assert.Equal(0, TiledImageSource.LevelForZoom(1.0));
        Assert.Equal(0, TiledImageSource.LevelForZoom(0.6));
        Assert.Equal(-1, TiledImageSource.LevelForZoom(0.5));
        Assert.Equal(-1, TiledImageSource.LevelForZoom(0.3));
        Assert.Equal(-2, TiledImageSource.LevelForZoom(0.25));
        Assert.Equal(-2, TiledImageSource.LevelForZoom(0.02));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task TilesMatchTheOrientedSource(int orientation)
    {
        var folder = Path.Combine(Path.GetTempPath(), "wc_image_viewer_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "pattern.png");
            TestImages.CreatePatternPng(path, 70, 50);

            var expected = StaRunner.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Orient(ImageLoader.LoadDocument(stream).Image, orientation);
            });

            var tiles = new TiledImageSource(() => File.OpenRead(path), 70, 50, orientation);
            var tile = await tiles.GetTileAsync(0, 0, 0, CancellationToken.None);

            Assert.NotNull(tile);
            Assert.Equal(
                (expected.PixelWidth, expected.PixelHeight),
                (tile!.Bitmap.PixelWidth, tile.Bitmap.PixelHeight));
            Assert.Equal(TestImages.GetPixels(expected), TestImages.GetPixels(tile.Bitmap));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static BitmapSource Orient(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(1, -1) } },
            8 => new RotateTransform(270),
            _ => null
        };

        if (transform is null)
        {
            return source;
        }

        var transformed = new TransformedBitmap(source, transform);
        transformed.Freeze();
        return transformed;
    }

    [Fact]
    public async Task TilesComposeIntoTheOrientedImageForEveryOrientation()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wc_image_viewer_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "pattern.png");
            TestImages.CreatePatternPng(path, 700, 500);

            for (var orientation = 1; orientation <= 8; orientation++)
            {
                var expected = StaRunner.Run(() =>
                {
                    using var stream = File.OpenRead(path);
                    return Orient(ImageLoader.LoadDocument(stream).Image, orientation);
                });

                var tiles = new TiledImageSource(() => File.OpenRead(path), 700, 500, orientation);
                var first = await tiles.GetTileAsync(0, 0, 0, CancellationToken.None);
                var second = await tiles.GetTileAsync(1, 0, 0, CancellationToken.None);
                Assert.NotNull(first);
                Assert.NotNull(second);

                var rendered = StaRunner.Run(() =>
                {
                    var visual = new DrawingVisual();
                    using (var context = visual.RenderOpen())
                    {
                        context.DrawImage(
                            first!.Bitmap,
                            new Rect(first.Left, first.Top, first.Width, first.Height));
                        context.DrawImage(
                            second!.Bitmap,
                            new Rect(second.Left, second.Top, second.Width, second.Height));
                    }

                    var target = new RenderTargetBitmap(
                        expected.PixelWidth,
                        expected.PixelHeight,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    target.Render(visual);
                    target.Freeze();

                    var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
                    converted.Freeze();
                    return (BitmapSource)converted;
                });

                Assert.Equal(
                    (expected.PixelWidth, expected.PixelHeight),
                    (rendered.PixelWidth, rendered.PixelHeight));
                Assert.Equal(TestImages.GetPixels(expected), TestImages.GetPixels(rendered));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task DecodesTilesAndReportsOrientedPositions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wc_image_viewer_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "tile.png");
            TestImages.CreatePng(path, 700, 500);
            var tiles = new TiledImageSource(() => File.OpenRead(path), 700, 500, orientation: 6);

            Assert.Equal(500, tiles.PixelWidth);
            Assert.Equal(700, tiles.PixelHeight);
            Assert.Equal((2, 1), tiles.GetTileGrid(0));

            var first = await tiles.GetTileAsync(0, 0, 0, CancellationToken.None);
            Assert.NotNull(first);
            Assert.Equal((0, 0), (first!.Left, first.Top));
            Assert.Equal((500, 512), (first.Width, first.Height));
            Assert.Equal((500, 512), (first.Bitmap.PixelWidth, first.Bitmap.PixelHeight));

            var second = await tiles.GetTileAsync(1, 0, 0, CancellationToken.None);
            Assert.NotNull(second);
            Assert.Equal((0, 512), (second!.Left, second.Top));
            Assert.Equal((500, 188), (second.Width, second.Height));
            Assert.Equal((500, 188), (second.Bitmap.PixelWidth, second.Bitmap.PixelHeight));

            var visible = tiles.GetVisibleTiles(0, 0, 500, 700, 0).ToArray();
            Assert.Equal(new[] { (0, 0), (1, 0) }, visible);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task DownscaledTileFitsItsOriginalImageArea()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wc_image_viewer_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "tile.png");
            TestImages.CreatePng(path, 700, 500);
            var tiles = new TiledImageSource(() => File.OpenRead(path), 700, 500, orientation: 1);

            Assert.Equal((1, 1), tiles.GetTileGrid(-1));

            var tile = await tiles.GetTileAsync(0, 0, -1, CancellationToken.None);
            Assert.NotNull(tile);
            Assert.Equal((0, 0), (tile!.Left, tile.Top));
            Assert.Equal((700, 500), (tile.Width, tile.Height));
            Assert.Equal((350, 250), (tile.Bitmap.PixelWidth, tile.Bitmap.PixelHeight));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
