using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Imazen.WebP;

namespace WcImageViewer.Services;

/// <summary>
/// <see cref="BitmapSource"/> 를 선택 형식으로 인코딩해 파일로 저장한다.
/// 저장은 <see cref="File.Create(string)"/> 로 하며, 인코딩 중 발생한 예외는 호출자에게 그대로 던진다.
/// 호출자 객체를 바꾸지 않기 위해 저장 전에 <see cref="BitmapSource.Freeze"/> 를 호출하지 않는다.
/// </summary>
public static class ImageEncoderService
{
    public static void Save(
        BitmapSource source,
        SaveFormatDefinition format,
        ImageSaveOptions options,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (format.Format == SaveImageFormat.WebP)
            {
                SaveWebP(source, options, filePath, cancellationToken);
                return;
            }

            var encoder = format.CreateEncoder(options)
                ?? throw new NotSupportedException($"형식 {format.DisplayName} 의 인코더를 만들 수 없습니다.");

            cancellationToken.ThrowIfCancellationRequested();
            encoder.Frames.Add(BitmapFrame.Create(source));

            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.Create(filePath);
            encoder.Save(stream);
        }
        catch (OperationCanceledException)
        {
            DeleteIncompleteFile(filePath);
            throw;
        }
    }

    public static void SaveFrames(
        IReadOnlyList<(BitmapSource Frame, int DelayMs)> frames,
        SaveFormatDefinition format,
        ImageSaveOptions options,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (frames.Count == 0)
        {
            throw new InvalidDataException("프레임이 없습니다.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (frames.Count == 1 || !format.SupportsAnimation)
            {
                Save(frames[0].Frame, format, options, filePath, cancellationToken);
                return;
            }

            if (format.Format == SaveImageFormat.Gif)
            {
                SaveGifFrames(frames, filePath, cancellationToken);
                return;
            }

            if (format.Format == SaveImageFormat.WebP)
            {
                SaveWebPFrames(frames, options, filePath, cancellationToken);
                return;
            }

            Save(frames[0].Frame, format, options, filePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            DeleteIncompleteFile(filePath);
            throw;
        }
    }

    private static void DeleteIncompleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best effort: leave the original cancellation exception visible.
        }
    }

    private static void SaveWebP(BitmapSource source, ImageSaveOptions options, string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("WebP 로 저장할 이미지가 비어 있습니다.");
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        cancellationToken.ThrowIfCancellationRequested();
        var config = new WebPEncoderConfig();
        config.SetQuality(Math.Clamp(options.WebPQuality, 1, 100));
        config.SetLossless(options.WebPLossless);

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.Create(filePath);
        WebPEncoder.Encode(pixels, width, height, stride, WebPPixelFormat.Bgra, config, stream);
    }

    private static void SaveGifFrames(
        IReadOnlyList<(BitmapSource Frame, int DelayMs)> frames,
        string filePath,
        CancellationToken cancellationToken)
    {
        var encoder = new GifBitmapEncoder();
        foreach (var (frame, delayMs) in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = new BitmapMetadata("gif");
            var delay = (ushort)Math.Clamp(delayMs / 10, 1, 65535);
            metadata.SetQuery("/grctlext/Delay", delay);
            var bitmapFrame = BitmapFrame.Create(frame, null, metadata, null);
            encoder.Frames.Add(bitmapFrame);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }

    private static void SaveWebPFrames(
        IReadOnlyList<(BitmapSource Frame, int DelayMs)> frames,
        ImageSaveOptions options,
        string filePath,
        CancellationToken cancellationToken)
    {
        var first = frames[0].Frame;
        var width = first.PixelWidth;
        var height = first.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("WebP 로 저장할 이미지가 비어 있습니다.");
        }

        var stride = width * 4;
        var config = new WebPEncoderConfig();
        config.SetQuality(Math.Clamp(options.WebPQuality, 1, 100));
        config.SetLossless(options.WebPLossless);

        var animEncoder = new AnimEncoder(width, height);
        var timestampMs = 0;
        try
        {
            foreach (var (frame, delayMs) in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BitmapSource sourceToConvert = frame;
                if (frame.PixelWidth != width || frame.PixelHeight != height)
                {
                    sourceToConvert = new TransformedBitmap(
                        frame,
                        new System.Windows.Media.ScaleTransform(
                            (double)width / frame.PixelWidth,
                            (double)height / frame.PixelHeight));
                }

                var converted = new FormatConvertedBitmap(sourceToConvert, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                animEncoder.AddFrame(pixels, stride, WebPPixelFormat.Bgra, timestampMs, config);
                timestampMs += Math.Max(20, delayMs);
            }

            using var stream = File.Create(filePath);
            animEncoder.Assemble(stream);
        }
        finally
        {
            (animEncoder as IDisposable)?.Dispose();
        }
    }
}
