using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

public static class ImageLoader
{
    private const long MaxAnimatedPixels = 250_000_000;

    public static BitmapSource Load(string path, CancellationToken cancellationToken = default)
    {
        using var stream = OpenFile(path);
        return LoadDocument(stream, cancellationToken).Image;
    }

    public static ImageDocument LoadDocument(string path, CancellationToken cancellationToken = default)
    {
        using var stream = OpenFile(path);
        return LoadDocument(stream, cancellationToken);
    }

    public static ImageDocument LoadDocument(Stream stream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!stream.CanSeek)
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return LoadDocument(copy, cancellationToken);
        }

        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.IgnoreImageCache,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("이미지 프레임이 없습니다.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var orientation = GetOrientation(decoder.Frames[0]);

        if (decoder is GifBitmapDecoder && decoder.Frames.Count > 1 && CanLoadAllFrames(decoder.Frames))
        {
            var frames = new BitmapSource[decoder.Frames.Count];
            var delays = new TimeSpan[decoder.Frames.Count];

            for (var i = 0; i < decoder.Frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                frames[i] = ApplyOrientation(decoder.Frames[i], orientation);
                frames[i].Freeze();
                delays[i] = GetGifDelay(decoder.Frames[i]);
            }

            return new ImageDocument(frames[0], frames, delays);
        }

        var image = ApplyOrientation(decoder.Frames[0], orientation);
        image.Freeze();
        return new ImageDocument(image, new[] { image }, new[] { TimeSpan.Zero });
    }

    public static BitmapSource LoadThumbnail(
        Stream stream,
        int decodePixelWidth,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.IgnoreImageCache,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("이미지 프레임이 없습니다.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var frame = decoder.Frames[0];
        var oriented = ApplyOrientation(frame, GetOrientation(frame));

        if (decodePixelWidth > 0)
        {
            var longestSide = Math.Max(oriented.PixelWidth, oriented.PixelHeight);
            if (longestSide > decodePixelWidth)
            {
                var scale = (double)decodePixelWidth / longestSide;
                var scaled = new TransformedBitmap(oriented, new ScaleTransform(scale, scale));
                scaled.Freeze();
                return scaled;
            }
        }

        oriented.Freeze();
        return oriented;
    }

    private static FileStream OpenFile(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);
    }

    private static bool CanLoadAllFrames(IReadOnlyList<BitmapFrame> frames)
    {
        if (frames.Count > 500)
        {
            return false;
        }

        var pixels = (long)frames[0].PixelWidth * frames[0].PixelHeight * frames.Count;
        return pixels <= MaxAnimatedPixels;
    }

    private static TimeSpan GetGifDelay(BitmapFrame frame)
    {
        if (frame.Metadata is BitmapMetadata metadata)
        {
            try
            {
                var value = metadata.GetQuery("/grctlext/Delay");
                if (value is ushort delay && delay > 0)
                {
                    return TimeSpan.FromMilliseconds(Math.Clamp(delay * 10, 20, 1000));
                }
            }
            catch (NotSupportedException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        return TimeSpan.FromMilliseconds(100);
    }

    private static int GetOrientation(BitmapSource source)
    {
        BitmapMetadata? metadata;
        try
        {
            metadata = source.Metadata as BitmapMetadata;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return 1;
        }

        if (metadata is null)
        {
            return 1;
        }

        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                var value = metadata.GetQuery(query);
                var orientation = value switch
                {
                    ushort u => u,
                    byte b => b,
                    int i => i,
                    uint ui => (int)ui,
                    _ => 1
                };
                if (orientation is >= 1 and <= 8)
                {
                    return orientation;
                }
            }
            catch (NotSupportedException)
            {
                // Some codecs do not expose this metadata path.
            }
            catch (ArgumentException)
            {
                // Ignore malformed metadata.
            }
        }

        return 1;
    }

    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
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
}
