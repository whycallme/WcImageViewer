using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WcImageViewer.Models;

namespace WcImageViewer.Controls;

public partial class ThumbnailItem : ObservableObject
{
    public ThumbnailItem(int index, ImageEntry entry)
    {
        Index = index;
        Entry = entry;
    }

    public int Index { get; }

    public ImageEntry Entry { get; }

    public string DisplayName => Entry.DisplayName;

    [ObservableProperty]
    private BitmapSource? _thumbnail;
}
