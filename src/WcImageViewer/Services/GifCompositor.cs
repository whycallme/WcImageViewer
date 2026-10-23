using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WcImageViewer.Services;

/// <summary>
/// Composites GIF frames the way a browser does. WIC returns only the pixels changed by
/// each frame, leaving the rest transparent; drawing those frames directly shows the
/// viewer background through the animation.
/// </summary>
public static class GifCompositor
{
    public static BitmapSource[] Compose(
        BitmapDecoder decoder,
        Stream stream,
        int orientation,
        CancellationToken cancellationToken)
    {
        if (decoder.Frames.Count == 0)
        {
            return Array.Empty<BitmapSource>();
        }

        var width = decoder.Frames[0].PixelWidth;
        var height = decoder.Frames[0].PixelHeight;
        var canvas = new byte[width * height * 4];
        var background = ReadBackgroundColor(stream);
        Fill(canvas, background);

        var result = new BitmapSource[decoder.Frames.Count];
        byte[]? previous = null;

        for (var i = 0; i < decoder.Frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frame = decoder.Frames[i];
            var disposal = GetDisposal(frame);
            if (disposal == 3)
            {
                previous = (byte[])canvas.Clone();
            }

            var framePixels = GetPixels(frame);
            var left = 0;
            var top = 0;

            if (frame.PixelWidth != width || frame.PixelHeight != height)
            {
                left = GetMetadataInt(frame, "/imgdesc/Left", 0);
                top = GetMetadataInt(frame, "/imgdesc/Top", 0);
            }

            Blend(
                canvas,
                width,
                height,
                framePixels,
                frame.PixelWidth,
                frame.PixelHeight,
                left,
                top);

            BitmapSource bitmap = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                canvas,
                width * 4);
            bitmap = ImageLoader.ApplyOrientation(bitmap, orientation);
            bitmap.Freeze();
            result[i] = bitmap;

            if (disposal == 2)
            {
                Clear(
                    canvas,
                    width,
                    height,
                    left,
                    top,
                    frame.PixelWidth,
                    frame.PixelHeight,
                    background);
            }
            else if (disposal == 3 && previous is not null)
            {
                canvas = previous;
                previous = null;
            }
        }

        return result;
    }

    private static byte[] GetPixels(BitmapSource frame)
    {
        var stride = frame.PixelWidth * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static void Blend(
        byte[] canvas,
        int canvasWidth,
        int canvasHeight,
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int left,
        int top)
    {
        var sourceStride = sourceWidth * 4;
        var startY = Math.Max(0, top);
        var endY = Math.Min(canvasHeight, top + sourceHeight);
        var startX = Math.Max(0, left);
        var endX = Math.Min(canvasWidth, left + sourceWidth);

        for (var y = startY; y < endY; y++)
        {
            var sourceRow = (y - top) * sourceStride;
            var canvasRow = y * canvasWidth * 4;

            for (var x = startX; x < endX; x++)
            {
                var sourceOffset = sourceRow + (x - left) * 4;
                var alpha = source[sourceOffset + 3];

                if (alpha == 0)
                {
                    continue;
                }

                var canvasOffset = canvasRow + x * 4;
                if (alpha == 255)
                {
                    canvas[canvasOffset] = source[sourceOffset];
                    canvas[canvasOffset + 1] = source[sourceOffset + 1];
                    canvas[canvasOffset + 2] = source[sourceOffset + 2];
                    canvas[canvasOffset + 3] = 255;
                    continue;
                }

                var sourceAlpha = alpha / 255.0;
                var destinationAlpha = canvas[canvasOffset + 3] / 255.0;
                var outAlpha = sourceAlpha + destinationAlpha * (1 - sourceAlpha);

                for (var channel = 0; channel < 3; channel++)
                {
                    canvas[canvasOffset + channel] = (byte)Math.Round(
                        (source[sourceOffset + channel] * sourceAlpha +
                         canvas[canvasOffset + channel] * destinationAlpha * (1 - sourceAlpha)) /
                        outAlpha);
                }

                canvas[canvasOffset + 3] = (byte)Math.Round(outAlpha * 255);
            }
        }
    }

    private static void Clear(
        byte[] canvas,
        int canvasWidth,
        int canvasHeight,
        int left,
        int top,
        int width,
        int height,
        Color color)
    {
        var startY = Math.Max(0, top);
        var endY = Math.Min(canvasHeight, top + height);
        var startX = Math.Max(0, left);
        var endX = Math.Min(canvasWidth, left + width);
        var bgra = new[] { color.B, color.G, color.R, color.A };

        for (var y = startY; y < endY; y++)
        {
            var row = y * canvasWidth * 4;
            for (var x = startX; x < endX; x++)
            {
                var offset = row + x * 4;
                canvas[offset] = bgra[0];
                canvas[offset + 1] = bgra[1];
                canvas[offset + 2] = bgra[2];
                canvas[offset + 3] = bgra[3];
            }
        }
    }

    private static void Fill(byte[] canvas, Color color)
    {
        for (var offset = 0; offset < canvas.Length; offset += 4)
        {
            canvas[offset] = color.B;
            canvas[offset + 1] = color.G;
            canvas[offset + 2] = color.R;
            canvas[offset + 3] = color.A;
        }
    }

    private static int GetDisposal(BitmapSource frame)
    {
        return GetMetadataInt(frame, "/grctlext/Disposal", 0);
    }

    private static int GetMetadataInt(BitmapSource frame, string query, int fallback)
    {
        if (frame.Metadata is not BitmapMetadata metadata)
        {
            return fallback;
        }

        try
        {
            return metadata.GetQuery(query) switch
            {
                byte b => b,
                ushort u => u,
                int i => i,
                uint ui => (int)ui,
                _ => fallback
            };
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
        catch (ArgumentException)
        {
            return fallback;
        }
    }

    private static Color ReadBackgroundColor(Stream stream)
    {
        var origin = stream.Position;

        try
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[13];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length ||
                header[0] != 'G' || header[1] != 'I' || header[2] != 'F' ||
                (header[3] != '8') ||
                (header[4] != '7' && header[4] != '9') ||
                header[5] != 'a')
            {
                return Colors.Transparent;
            }

            var packed = header[10];
            if ((packed & 0x80) == 0)
            {
                return Colors.Transparent;
            }

            var tableSize = 3 << ((packed & 0x07) + 1);
            var table = new byte[tableSize];
            if (stream.ReadAtLeast(table, table.Length, throwOnEndOfStream: false) != table.Length)
            {
                return Colors.Transparent;
            }

            var index = header[11] * 3;
            return index + 2 < table.Length
                ? Color.FromArgb(255, table[index], table[index + 1], table[index + 2])
                : Colors.Transparent;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return Colors.Transparent;
        }
        finally
        {
            stream.Position = origin;
        }
    }
}
