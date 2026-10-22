using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer.Controls;

public partial class ThumbnailStrip : UserControl
{
    private readonly ObservableCollection<ThumbnailItem> _items = new();
    private readonly ThumbnailCache _cache = new(400);
    private readonly SemaphoreSlim _gate = new(3);
    private IImageSource? _source;
    private CancellationTokenSource? _cts;
    private bool _suppressSelection;

    public ThumbnailStrip()
    {
        InitializeComponent();
        List.ItemsSource = _items;
    }

    public event EventHandler<int>? SelectionChanged;

    public void SetSource(IImageSource source, int selectedIndex)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _cache.Clear();
        _source = source;

        _items.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            _items.Add(new ThumbnailItem(i, source.GetEntry(i)));
        }

        SelectIndex(selectedIndex);
        _ = LoadAroundAsync(selectedIndex);
    }

    public void SelectIndex(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        _suppressSelection = true;
        try
        {
            List.SelectedIndex = index;
            List.ScrollIntoView(_items[index]);
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    public void Clear()
    {
        _cts?.Cancel();
        _source = null;
        _items.Clear();
        _cache.Clear();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
        {
            return;
        }

        var index = List.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        SelectionChanged?.Invoke(this, index);
        _ = LoadAroundAsync(index);
    }

    private void OnItemLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ThumbnailItem item })
        {
            _ = LoadThumbnailAsync(item);
        }
    }

    // Wheel over the strip scrolls it sideways instead of changing image.
    // VirtualizingStackPanel scrolls logically, so offsets are in items.
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroll = FindScrollViewer(List);
        if (scroll is null || scroll.ScrollableWidth <= 0)
        {
            return;
        }

        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta / 120.0);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private async Task LoadAroundAsync(int index)
    {
        for (var offset = -3; offset <= 3; offset++)
        {
            var itemIndex = index + offset;
            if (itemIndex >= 0 && itemIndex < _items.Count)
            {
                await LoadThumbnailAsync(_items[itemIndex]);
            }
        }
    }

    private async Task LoadThumbnailAsync(ThumbnailItem item)
    {
        if (item.Thumbnail is not null)
        {
            return;
        }

        var source = _source;
        var token = _cts?.Token ?? CancellationToken.None;
        if (source is null)
        {
            return;
        }

        if (_cache.TryGet(item.Entry.Key, out var cached) && cached is not null)
        {
            item.Thumbnail = cached;
            return;
        }

        try
        {
            await _gate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (item.Thumbnail is not null || !ReferenceEquals(source, _source))
            {
                return;
            }

            using var stream = await source.OpenEntryAsync(item.Index, token);
            var thumbnail = await Task.Run(
                () => ImageLoader.LoadThumbnail(stream, 160, token),
                token);

            _cache.Set(item.Entry.Key, thumbnail);

            if (ReferenceEquals(source, _source))
            {
                item.Thumbnail = thumbnail;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Corrupt image: leave the placeholder in place.
        }
        finally
        {
            _gate.Release();
        }
    }
}
