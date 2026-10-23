using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

/// <summary>
/// Decodes a very large image one tile at a time. The encoded stream is reopened for every
/// tile so WIC can decode just the requested region instead of the whole bitmap.
/// Lower tile levels decode a larger source region and scale it down, keeping memory and
/// the number of on-screen tiles bounded while zoomed out.
/// </summary>
public sealed class TiledImageSource : IImageTileSource
{
    private const int DefaultTileSize = 512;
    private const int MinLevel = -2;
    private const int CacheCapacity = 32;

    private readonly Func<Stream> _openStream;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly int _orientation;
    private readonly Dictionary<(int Column, int Row, int Level), ImageTile> _cache = new();
    private readonly LinkedList<(int Column, int Row, int Level)> _lru = new();
    private readonly Dictionary<(int Column, int Row, int Level), Task<ImageTile?>> _pending = new();
    private readonly object _gate = new();

    public TiledImageSource(
        Func<Stream> openStream,
        int sourceWidth,
        int sourceHeight,
        int orientation)
    {
        _openStream = openStream;
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _orientation = orientation is >= 1 and <= 8 ? orientation : 1;

        var swapped = _orientation is 5 or 6 or 7 or 8;
        PixelWidth = swapped ? sourceHeight : sourceWidth;
        PixelHeight = swapped ? sourceWidth : sourceHeight;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int BaseTileSize => DefaultTileSize;

    /// <summary>Level 0 is full resolution; each lower level halves the rendered size.</summary>
    public static int LevelForZoom(double zoom)
    {
        if (zoom >= 1)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(zoom, 0.01))), MinLevel, 0);
    }

    public int GetLevelForZoom(double zoom) => LevelForZoom(zoom);

    public IEnumerable<(int Column, int Row)> GetVisibleTiles(
        double left,
        double top,
        double width,
        double height,
        int level)
    {
        level = Math.Clamp(level, MinLevel, 0);
        var matrix = ImageLoader.GetOrientationMatrix(_orientation, _sourceWidth, _sourceHeight);
        if (!matrix.HasInverse)
        {
            yield break;
        }

        matrix.Invert();
        var p1 = matrix.Transform(new Point(left, top));
        var p2 = matrix.Transform(new Point(left + width, top));
        var p3 = matrix.Transform(new Point(left, top + height));
        var p4 = matrix.Transform(new Point(left + width, top + height));

        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        var tileSize = SourceTileSize(level);
        var (columns, rows) = GetTileGrid(level);
        var firstColumn = Math.Clamp((int)Math.Floor(minX / tileSize), 0, columns - 1);
        var lastColumn = Math.Clamp((int)Math.Floor(maxX / tileSize), 0, columns - 1);
        var firstRow = Math.Clamp((int)Math.Floor(minY / tileSize), 0, rows - 1);
        var lastRow = Math.Clamp((int)Math.Floor(maxY / tileSize), 0, rows - 1);

        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                yield return (column, row);
            }
        }
    }

    public (int Columns, int Rows) GetTileGrid(int level)
    {
        var tileSize = SourceTileSize(level);
        return (
            (int)Math.Ceiling(_sourceWidth / (double)tileSize),
            (int)Math.Ceiling(_sourceHeight / (double)tileSize));
    }

    public Task<ImageTile?> GetTileAsync(
        int column,
        int row,
        int level,
        CancellationToken cancellationToken)
    {
        level = Math.Clamp(level, MinLevel, 0);
        var key = (column, row, level);

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                Touch(key);
                return Task.FromResult<ImageTile?>(cached);
            }

            if (_pending.TryGetValue(key, out var pending))
            {
                return pending;
            }

            var task = Task.Run(() => DecodeTile(column, row, level, cancellationToken), cancellationToken);
            _pending[key] = task;

            _ = task.ContinueWith(
                completed =>
                {
                    lock (_gate)
                    {
                        _pending.Remove(key);
                        if (completed.Status == TaskStatus.RanToCompletion &&
                            completed.Result is { } tile)
                        {
                            Cache(key, tile);
                        }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return task;
        }
    }

    private ImageTile? DecodeTile(
        int column,
        int row,
        int level,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tileSize = SourceTileSize(level);
            var x = column * tileSize;
            var y = row * tileSize;
            var width = Math.Min(tileSize, _sourceWidth - x);
            var height = Math.Min(tileSize, _sourceHeight - y);

            using var stream = _openStream();
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreImageCache,
                BitmapCacheOption.None);

            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            BitmapSource source = new CroppedBitmap(
                decoder.Frames[0],
                new Int32Rect(x, y, width, height));

            if (level < 0)
            {
                var scale = Math.Pow(2, level);
                source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            }

            var oriented = ImageLoader.ApplyOrientation(source, _orientation);
            var converted = new FormatConvertedBitmap(oriented, PixelFormats.Bgra32, null, 0);

            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            var bitmap = BitmapSource.Create(
                converted.PixelWidth,
                converted.PixelHeight,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);
            bitmap.Freeze();

            var (left, top) = GetTilePosition(x, y, width, height);
            var swapped = _orientation is 5 or 6 or 7 or 8;

            return new ImageTile(
                bitmap,
                left,
                top,
                swapped ? height : width,
                swapped ? width : height);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A single unreadable tile leaves the preview visible instead of failing the image.
            return null;
        }
    }

    private (int Left, int Top) GetTilePosition(int x, int y, int width, int height)
    {
        var matrix = ImageLoader.GetOrientationMatrix(_orientation, _sourceWidth, _sourceHeight);

        var p1 = matrix.Transform(new Point(x, y));
        var p2 = matrix.Transform(new Point(x + width, y));
        var p3 = matrix.Transform(new Point(x, y + height));
        var p4 = matrix.Transform(new Point(x + width, y + height));

        var left = (int)Math.Round(Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X)));
        var top = (int)Math.Round(Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y)));
        return (left, top);
    }

    private static int SourceTileSize(int level)
    {
        return DefaultTileSize << -Math.Clamp(level, MinLevel, 0);
    }

    private void Touch((int Column, int Row, int Level) key)
    {
        _lru.Remove(key);
        _lru.AddLast(key);
    }

    private void Cache((int Column, int Row, int Level) key, ImageTile tile)
    {
        if (_cache.ContainsKey(key))
        {
            Touch(key);
            return;
        }

        _cache[key] = tile;
        _lru.AddLast(key);

        while (_cache.Count > CacheCapacity && _lru.First is { } oldest)
        {
            _lru.RemoveFirst();
            _cache.Remove(oldest.Value);
        }
    }
}
