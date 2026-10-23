using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Imazen.WebP;

namespace WcImageViewer.Services;

/// <summary>
/// Decodes animated WebP frames on demand with libwebp. WIC can expose the frames, but
/// random access re-decodes from the first frame, which is unusable for long animations.
/// </summary>
public sealed class WebPFrameSource : IDisposable
{
    private readonly int _orientation;
    private readonly object _gate = new();

    private byte[]? _data;
    private AnimDecoder? _decoder;
    private int _decodedCount;
    private int _lastIndex = -1;
    private BitmapSource? _lastFrame;
    private bool _disposed;

    public WebPFrameSource(byte[] data, int orientation)
    {
        _data = data;
        _orientation = orientation is >= 1 and <= 8 ? orientation : 1;
        _decoder = new AnimDecoder(data);

        var info = _decoder.Info;
        SourceWidth = info.Width;
        SourceHeight = info.Height;
        FrameCount = info.FrameCount;
    }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public int FrameCount { get; }

    public BitmapSource GetFrame(int index)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (FrameCount <= 0)
            {
                throw new InvalidDataException("애니메이션 프레임이 없습니다.");
            }

            index = ((index % FrameCount) + FrameCount) % FrameCount;
            if (index == _lastIndex && _lastFrame is not null)
            {
                return _lastFrame;
            }

            if (_decoder is null)
            {
                throw new ObjectDisposedException(nameof(WebPFrameSource));
            }

            if (index == 0 || index < _decodedCount)
            {
                _decoder.Reset();
                _decodedCount = 0;
            }

            while (_decodedCount <= index)
            {
                if (!_decoder.HasMoreFrames())
                {
                    _decoder.Reset();
                    _decodedCount = 0;
                }

                var frame = _decoder.GetNextFrame()
                    ?? throw new InvalidDataException("WebP 프레임을 디코딩할 수 없습니다.");
                _decodedCount++;

                if (_decodedCount - 1 != index)
                {
                    continue;
                }

                var bitmap = ToBitmap(frame);
                _lastIndex = index;
                _lastFrame = bitmap;
                return bitmap;
            }

            throw new InvalidDataException("WebP 프레임을 찾을 수 없습니다.");
        }
    }

    private BitmapSource ToBitmap(AnimFrame frame)
    {
        var stride = frame.Width * 4;
        if (frame.Pixels.Length < stride * frame.Height)
        {
            throw new InvalidDataException("WebP 프레임 크기가 올바르지 않습니다.");
        }

        BitmapSource bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            frame.Pixels,
            stride);

        if (_orientation != 1)
        {
            bitmap = ImageLoader.ApplyOrientation(bitmap, _orientation);
        }

        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _decoder?.Dispose();
            _decoder = null;
            _lastFrame = null;
            _data = null;
        }
    }
}
