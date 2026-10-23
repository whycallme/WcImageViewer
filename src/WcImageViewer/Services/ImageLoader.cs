using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

public static class ImageLoader
{
    public const long TilingPixelThreshold = 100_000_000;

    private const long MaxAnimatedPixels = 250_000_000;
    private const int MaxSingleBitmapDimension = 16_384;
    private const int MaxPreviewSide = 4_096;
    private static readonly Guid WebPContainerFormat = new("e094b0e2-67f2-45b3-b0ea-115337ca7cf3");

    public static BitmapSource Load(string path, CancellationToken cancellationToken = default)
    {
        using var stream = OpenFile(path);
        return LoadDocument(stream, () => OpenFile(path), Path.GetExtension(path), cancellationToken).Image;
    }

    public static ImageDocument LoadDocument(string path, CancellationToken cancellationToken = default)
    {
        using var stream = OpenFile(path);
        return LoadDocument(stream, () => OpenFile(path), Path.GetExtension(path), cancellationToken);
    }

    public static ImageDocument LoadDocument(Stream stream, CancellationToken cancellationToken = default)
    {
        return LoadDocument(stream, null, null, cancellationToken);
    }

    public static ImageDocument LoadDocument(
        Stream stream,
        Func<Stream>? reopen,
        string? extension,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!stream.CanSeek)
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return LoadDocument(copy, null, extension, cancellationToken);
        }

        var info = ReadFrameInfo(stream, extension, cancellationToken);

        if (ShouldUseTiles(info.Width, info.Height))
        {
            return LoadTiled(stream, info, reopen, extension, cancellationToken);
        }

        if (info.IsWebP && info.FrameCount > 1)
        {
            return LoadWebP(stream, info, cancellationToken);
        }

        return LoadFull(stream, extension, cancellationToken);
    }

    public static BitmapSource LoadThumbnail(
        Stream stream,
        int decodePixelWidth,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!stream.CanSeek)
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return LoadThumbnail(copy, decodePixelWidth, cancellationToken);
        }

        var info = ReadFrameInfo(stream, null, cancellationToken);
        return LoadPreview(stream, info, Math.Max(1, decodePixelWidth), cancellationToken);
    }

    /// <summary>Large images are decoded as tiles so one bitmap never exceeds WIC/WPF limits.</summary>
    public static bool ShouldUseTiles(int width, int height)
    {
        return (long)width * height > TilingPixelThreshold ||
               Math.Max(width, height) > MaxSingleBitmapDimension;
    }

    private static ImageDocument LoadFull(
        Stream stream,
        string? extension,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var decoder = CreateDecoder(stream, BitmapCacheOption.OnLoad, extension);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("이미지 프레임이 없습니다.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var orientation = GetOrientation(decoder.Frames[0]);
        var isGif = decoder is GifBitmapDecoder;

        if (isGif && CanLoadAllFrames(decoder.Frames))
        {
            var frames = GifCompositor.Compose(decoder, stream, orientation, cancellationToken);
            var delays = new TimeSpan[frames.Length];

            for (var i = 0; i < frames.Length; i++)
            {
                delays[i] = GetGifDelay(decoder.Frames[i]);
            }

            return new ImageDocument(frames[0], frames, delays);
        }

        var image = ApplyOrientation(decoder.Frames[0], orientation);
        image.Freeze();
        return new ImageDocument(image, new[] { image }, new[] { TimeSpan.Zero });
    }

    private static ImageDocument LoadTiled(
        Stream stream,
        FrameInfo info,
        Func<Stream>? reopen,
        string? extension,
        CancellationToken cancellationToken)
    {
        var preview = LoadPreview(stream, info, MaxPreviewSide, cancellationToken);
        var openStream = reopen ?? CreateSnapshotFactory(stream);
        var tiles = new TiledImageSource(openStream, info.Width, info.Height, info.Orientation);

        return new ImageDocument(
            preview,
            new[] { preview },
            new[] { TimeSpan.Zero },
            tiles,
            tiles.PixelWidth,
            tiles.PixelHeight,
            info.DpiX,
            info.DpiY);
    }

    private static ImageDocument LoadWebP(
        Stream stream,
        FrameInfo info,
        CancellationToken cancellationToken)
    {
        var data = ReadAllBytes(stream);
        using var delayStream = new MemoryStream(data, writable: false);
        var delays = WebPAnimation.ReadFrameDelays(delayStream, info.FrameCount);

        var source = new WebPFrameSource(data, info.Orientation);

        try
        {
            if (source.FrameCount != delays.Length)
            {
                using var exactStream = new MemoryStream(data, writable: false);
                delays = WebPAnimation.ReadFrameDelays(exactStream, source.FrameCount);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var first = source.GetFrame(0);
            var swapped = info.Orientation is 5 or 6 or 7 or 8;

            return new ImageDocument(
                first,
                new[] { first },
                delays,
                pixelWidth: swapped ? source.SourceHeight : source.SourceWidth,
                pixelHeight: swapped ? source.SourceWidth : source.SourceHeight,
                dpiX: info.DpiX,
                dpiY: info.DpiY,
                frameLoader: source.GetFrame,
                frameCount: source.FrameCount,
                lifetime: source);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static BitmapSource LoadPreview(
        Stream stream,
        FrameInfo info,
        int maxSide,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;

        var swapped = info.Orientation is 5 or 6 or 7 or 8;
        var orientedWidth = swapped ? info.Height : info.Width;
        var orientedHeight = swapped ? info.Width : info.Height;

        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;

        if (Math.Max(orientedWidth, orientedHeight) > maxSide)
        {
            // DecodePixelWidth refers to the encoded image, which is rotated for swapped orientations.
            var useWidth = (orientedWidth >= orientedHeight) ^ swapped;
            if (useWidth)
            {
                image.DecodePixelWidth = maxSide;
            }
            else
            {
                image.DecodePixelHeight = maxSide;
            }
        }

        image.EndInit();
        cancellationToken.ThrowIfCancellationRequested();
        image.Freeze();

        var oriented = ApplyOrientation(image, info.Orientation);
        oriented.Freeze();
        return oriented;
    }

    private static Func<Stream> CreateSnapshotFactory(Stream stream)
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.ToArray();
        return () => new MemoryStream(bytes, writable: false);
    }

    private static FrameInfo ReadFrameInfo(
        Stream stream,
        string? extension,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var decoder = CreateDecoder(stream, BitmapCacheOption.None, extension);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("이미지 프레임이 없습니다.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var frame = decoder.Frames[0];
        var isWebP = string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase) ||
                     decoder.CodecInfo?.ContainerFormat == WebPContainerFormat;

        return new FrameInfo(
            frame.PixelWidth,
            frame.PixelHeight,
            GetOrientation(frame),
            frame.DpiX,
            frame.DpiY,
            decoder.Frames.Count,
            isWebP);
    }

    private static BitmapDecoder CreateDecoder(
        Stream stream,
        BitmapCacheOption cacheOption,
        string? extension)
    {
        try
        {
            return BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreImageCache,
                cacheOption);
        }
        catch (NotSupportedException) when (IsOptionalCodec(extension))
        {
            throw new NotSupportedException(OptionalCodecMessage(extension));
        }
    }

    internal static FileStream OpenFile(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);
    }

    private static bool IsOptionalCodec(string? extension)
    {
        return extension?.ToLowerInvariant() is ".webp" or ".avif" or ".heic" or ".heif";
    }

    private static string OptionalCodecMessage(string? extension)
    {
        return extension?.ToLowerInvariant() switch
        {
            ".webp" => "WebP 이미지를 열려면 Windows의 'WebP 이미지 확장'이 필요합니다.\nMicrosoft Store에서 설치한 뒤 다시 시도하세요.",
            ".avif" => "AVIF 이미지를 열려면 Microsoft Store의 'AVIF 이미지 확장'이 필요합니다.",
            ".heic" or ".heif" => "HEIC/HEIF 이미지를 열려면 Microsoft Store의 'HEIF 이미지 확장'과 'HEVC 비디오 확장'이 필요합니다.",
            _ => "이 이미지 형식을 지원하는 Windows 이미지 코덱이 설치되어 있지 않습니다."
        };
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

    internal static Matrix GetOrientationMatrix(int orientation, int width, int height)
    {
        return orientation switch
        {
            2 => new Matrix(-1, 0, 0, 1, width, 0),
            3 => new Matrix(-1, 0, 0, -1, width, height),
            4 => new Matrix(1, 0, 0, -1, 0, height),
            5 => new Matrix(0, 1, 1, 0, 0, 0),
            6 => new Matrix(0, 1, -1, 0, height, 0),
            7 => new Matrix(0, -1, -1, 0, height, width),
            8 => new Matrix(0, -1, 1, 0, 0, width),
            _ => Matrix.Identity
        };
    }

    internal static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
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

    private readonly record struct FrameInfo(
        int Width,
        int Height,
        int Orientation,
        double DpiX,
        double DpiY,
        int FrameCount,
        bool IsWebP);
}
