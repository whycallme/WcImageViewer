using System.Buffers.Binary;
using System.IO;

namespace WcImageViewer.Services;

/// <summary>
/// Reads per-frame durations from the WebP ANMF chunks. WIC exposes the frames but not
/// the animation timing, so the small RIFF container is parsed directly.
/// </summary>
public static class WebPAnimation
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(100);

    public static TimeSpan[] ReadFrameDelays(Stream stream, int frameCount)
    {
        var delays = new TimeSpan[Math.Max(0, frameCount)];
        Array.Fill(delays, DefaultDelay);

        if (delays.Length == 0 || !stream.CanSeek)
        {
            return delays;
        }

        var origin = stream.Position;

        try
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[12];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length ||
                header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F' ||
                header[8] != 'W' || header[9] != 'E' || header[10] != 'B' || header[11] != 'P')
            {
                return delays;
            }

            var frame = 0;
            Span<byte> chunk = stackalloc byte[8];
            Span<byte> frameHeader = stackalloc byte[16];

            while (frame < delays.Length &&
                   stream.ReadAtLeast(chunk, chunk.Length, throwOnEndOfStream: false) == chunk.Length)
            {
                var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                var isAnimationFrame =
                    chunk[0] == 'A' && chunk[1] == 'N' && chunk[2] == 'M' && chunk[3] == 'F';

                if (isAnimationFrame)
                {
                    if (size < 16)
                    {
                        break;
                    }

                    if (stream.ReadAtLeast(frameHeader, frameHeader.Length, throwOnEndOfStream: false) != frameHeader.Length)
                    {
                        break;
                    }

                    var milliseconds =
                        frameHeader[12] | (frameHeader[13] << 8) | (frameHeader[14] << 16);
                    delays[frame++] = milliseconds <= 0
                        ? DefaultDelay
                        : TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 20, 1000));

                    stream.Seek(size - 16L + (size & 1), SeekOrigin.Current);
                }
                else
                {
                    stream.Seek((long)size + (size & 1), SeekOrigin.Current);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            // Malformed containers fall back to the default delay.
        }
        finally
        {
            stream.Position = origin;
        }

        return delays;
    }
}
