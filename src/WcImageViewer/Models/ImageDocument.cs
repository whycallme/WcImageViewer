using System.Windows.Media.Imaging;

namespace WcImageViewer.Models;

public sealed class ImageDocument
{
    public ImageDocument(
        BitmapSource image,
        IReadOnlyList<BitmapSource> frames,
        IReadOnlyList<TimeSpan> delays)
    {
        Image = image;
        Frames = frames;
        Delays = delays;
    }

    public BitmapSource Image { get; }

    public IReadOnlyList<BitmapSource> Frames { get; }

    public IReadOnlyList<TimeSpan> Delays { get; }

    public bool IsAnimated => Frames.Count > 1;
}
