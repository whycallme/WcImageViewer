using System.Windows.Media.Imaging;

namespace WcImageViewer.Models;

public sealed class ImageDocument : IDisposable
{
    private readonly Func<int, BitmapSource>? _frameLoader;
    private readonly IDisposable? _lifetime;

    public ImageDocument(
        BitmapSource image,
        IReadOnlyList<BitmapSource> frames,
        IReadOnlyList<TimeSpan> delays,
        IImageTileSource? tiles = null,
        int? pixelWidth = null,
        int? pixelHeight = null,
        double? dpiX = null,
        double? dpiY = null,
        Func<int, BitmapSource>? frameLoader = null,
        int? frameCount = null,
        IDisposable? lifetime = null)
    {
        Image = image;
        Frames = frames;
        Delays = delays;
        Tiles = tiles;
        PixelWidth = pixelWidth ?? image.PixelWidth;
        PixelHeight = pixelHeight ?? image.PixelHeight;
        DpiX = dpiX ?? image.DpiX;
        DpiY = dpiY ?? image.DpiY;
        _frameLoader = frameLoader;
        _lifetime = lifetime;
        FrameCount = frameCount ?? frames.Count;
    }

    /// <summary>The display image. For tiled documents this is a downscaled preview.</summary>
    public BitmapSource Image { get; }

    public IReadOnlyList<BitmapSource> Frames { get; }

    public IReadOnlyList<TimeSpan> Delays { get; }

    /// <summary>Full-resolution tile provider for images above the tiling threshold.</summary>
    public IImageTileSource? Tiles { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public double DpiX { get; }

    public double DpiY { get; }

    public int FrameCount { get; }

    public bool IsTiled => Tiles is not null;

    public bool IsAnimated => FrameCount > 1;

    public BitmapSource GetFrame(int index)
    {
        return _frameLoader is null ? Frames[index] : _frameLoader(index);
    }

    public void Dispose()
    {
        _lifetime?.Dispose();
    }
}
