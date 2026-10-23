using System.Windows.Media.Imaging;

namespace WcImageViewer.Models;

/// <summary>A decoded tile and its placement in the oriented image pixel space.</summary>
public sealed record ImageTile(BitmapSource Bitmap, int Left, int Top, int Width, int Height);

/// <summary>Provides full-resolution tiles for images too large to decode in one piece.</summary>
public interface IImageTileSource
{
    /// <summary>Width of the oriented image.</summary>
    int PixelWidth { get; }

    /// <summary>Height of the oriented image.</summary>
    int PixelHeight { get; }

    /// <summary>Tile edge length at level 0, in image pixels.</summary>
    int BaseTileSize { get; }

    /// <summary>Tile grid for a level; level 0 is full resolution, lower levels are downscaled.</summary>
    (int Columns, int Rows) GetTileGrid(int level);

    int GetLevelForZoom(double zoom);

    /// <summary>Source tiles touching a rectangle in oriented image pixel coordinates.</summary>
    IEnumerable<(int Column, int Row)> GetVisibleTiles(
        double left,
        double top,
        double width,
        double height,
        int level);

    Task<ImageTile?> GetTileAsync(int column, int row, int level, CancellationToken cancellationToken);
}
