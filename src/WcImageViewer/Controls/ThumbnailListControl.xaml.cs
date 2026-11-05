using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WcImageViewer.Services;

namespace WcImageViewer.Controls;

public partial class ThumbnailListControl : UserControl
{
    private readonly Dictionary<Image, CancellationTokenSource> _imageTokens = new();
    private VirtualizingWrapPanel? _wrapPanel;
    private bool _mouseDrivenSelection;

    public ThumbnailListControl()
    {
        InitializeComponent();
        TileSize = 96;
        List.SelectionChanged += OnListSelectionChanged;
        List.PreviewMouseLeftButtonDown += OnListPreviewMouseLeftButtonDown;
        List.MouseLeftButtonUp += (_, _) => _mouseDrivenSelection = false;
        Loaded += (_, _) => UpdatePanelTileSize();
    }

    public static readonly DependencyProperty TileSizeProperty =
        DependencyProperty.Register(
            nameof(TileSize),
            typeof(int),
            typeof(ThumbnailListControl),
            new PropertyMetadata(96, OnTileSizeChanged, CoerceTileSize));

    public int TileSize
    {
        get => (int)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    private static object CoerceTileSize(DependencyObject d, object baseValue)
    {
        var v = (int)baseValue;
        return Math.Clamp(v, 64, 320);
    }

    public static readonly DependencyProperty ItemFontSizeProperty =
        DependencyProperty.Register(
            nameof(ItemFontSize),
            typeof(double),
            typeof(ThumbnailListControl),
            new PropertyMetadata(12.0, (d, _) => ((ThumbnailListControl)d).UpdatePanelTileSize()));

    public double ItemFontSize
    {
        get => (double)GetValue(ItemFontSizeProperty);
        set => SetValue(ItemFontSizeProperty, value);
    }

    private static void OnTileSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ThumbnailListControl)d).UpdatePanelTileSize();
    }

    public Func<string, int, CancellationToken, Task<BitmapSource?>>? ThumbnailProvider { get; set; }

    public string? FocusedPath { get; private set; }
    public string? LastClickedPath { get; private set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler<string>? ItemActivated;
    public event EventHandler? EmptyAreaDoubleClicked;
    public event Action<int>? CtrlWheelZoom;

    public void SetItems(IReadOnlyList<ThumbItem> items)
    {
        List.ItemsSource = items;
    }

    public IReadOnlyList<string> SelectedPaths =>
        List.SelectedItems.Cast<ThumbItem>().Select(t => t.Path).ToList().AsReadOnly();

    /// <summary>
    /// path 와 (정규화·대소문자 무시로) 같은 항목을 선택하고 화면에 보이게 한 뒤 포커스를 준다.
    /// 항목이 없으면 false. 컨테이너가 아직 없으면 ContextIdle 에 한 번 더 시도한다.
    /// </summary>
    public bool SelectPath(string path)
    {
        if (List.ItemsSource is not IEnumerable<ThumbItem> items)
        {
            return false;
        }

        var list = items.ToList();
        var index = ResolveSelectionIndex(list.Select(t => t.Path).ToList(), path);
        if (index < 0)
        {
            return false;
        }

        List.SelectedIndex = index;
        FocusedPath = list[index].Path;
        ScrollAndFocus(index, retry: true);
        return true;
    }

    /// <summary>선택 대상 인덱스 결정(순수 함수). 대소문자·정규화만 다른 경로도 같은 항목으로 본다. 없으면 -1.</summary>
    public static int ResolveSelectionIndex(IReadOnlyList<string> itemPaths, string? targetPath) =>
        PathMatcher.IndexOf(itemPaths, targetPath);

    private void ScrollAndFocus(int index, bool retry)
    {
        if (!IsLoaded || index < 0 || index >= List.Items.Count)
        {
            return;
        }

        List.UpdateLayout();
        List.ScrollIntoView(List.Items[index]);
        List.UpdateLayout();

        if (List.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
        {
            container.Focus();
            return;
        }

        List.Focus();
        if (retry)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => ScrollAndFocus(index, retry: false)));
        }
    }

    private void OnWrapPanelLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is VirtualizingWrapPanel panel)
        {
            _wrapPanel = panel;
            UpdatePanelTileSize();
        }
    }

    private void UpdatePanelTileSize()
    {
        if (_wrapPanel is not null)
        {
            var slot = ComputeSlotSize(TileSize, ItemFontSize, MeasureLineHeight(FontFamily, ItemFontSize));
            _wrapPanel.ItemWidth = slot.Width;
            _wrapPanel.ItemHeight = slot.Height;
        }
    }

    /// <summary>
    /// 아이템 1개가 차지해야 하는 슬롯 크기(픽셀). 아이템 컨테이너 크롬(Margin 2 + Border 2 + Padding 2, 가로·세로 각 12),
    /// 썸네일 영역(폭 tile+8, 높이 tile+32), 캡션 Margin 상단 2, 캡션 2줄(해상도/용량 줄, 파일명 줄), 여유 4 를 모두 더한다.
    /// lineHeight 는 한 줄의 실제 높이이며, fontSize*1.4 보다 작으면 그 값을 쓴다.
    /// </summary>
    public static Size ComputeSlotSize(double tileSize, double fontSize, double lineHeight)
    {
        const double chrome = 12;      // ItemContainer Margin 2 + Border 2 + Padding 2, 좌우 또는 상하 합계
        const double captionTop = 2;   // 캡션 Grid Margin="0,2,0,0"
        const double slack = 4;        // 여유
        var line = Math.Ceiling(Math.Max(lineHeight, fontSize * 1.4));
        var width = tileSize + 8 + chrome + slack;
        var height = tileSize + 32 + captionTop + 2 * line + chrome + slack;
        return new Size(width, height);
    }

    /// <summary>폰트 패밀리와 크기로부터 한 줄의 실제 높이(픽셀)를 구한다.</summary>
    public static double MeasureLineHeight(FontFamily family, double fontSize)
    {
        return family.LineSpacing * fontSize;
    }

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var item = FindAncestor<ListBoxItem>(dep);
            if (item?.DataContext is ThumbItem thumb)
            {
                LastClickedPath = thumb.Path;
                FocusedPath = thumb.Path;
                _mouseDrivenSelection = true;
                return;
            }
        }
        _mouseDrivenSelection = false;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var mouseDriven = _mouseDrivenSelection || Mouse.LeftButton == MouseButtonState.Pressed;
        if (mouseDriven)
        {
            // 마우스 클릭으로 인한 선택 변경: Preview 에서 기록한 LastClickedPath 를 덮어쓰지 않는다.
        }
        else
        {
            // 키보드/프로그램적 선택: 포커스 항목으로 갱신한다.
            if (List.SelectedItem is ThumbItem item)
            {
                FocusedPath = item.Path;
                LastClickedPath = null;
            }
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnListMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ListBoxItem? listBoxItem = null;
        if (e.OriginalSource is DependencyObject dep)
        {
            listBoxItem = FindAncestor<ListBoxItem>(dep);
        }
        if (listBoxItem is null)
        {
            EmptyAreaDoubleClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (List.SelectedItem is ThumbItem item)
        {
            FocusedPath = item.Path;
            ItemActivated?.Invoke(this, item.Path);
        }
    }

    private void OnListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var path = ResolveEnterActivationPath(FocusedPath, List.SelectedItem as ThumbItem);
        if (path is not null)
        {
            FocusedPath = path;
            ItemActivated?.Invoke(this, path);
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        _mouseDrivenSelection = false;
        if (e.Key == Key.Enter)
        {
            var path = ResolveEnterActivationPath(FocusedPath, List.SelectedItem as ThumbItem);
            if (path is not null)
            {
                ItemActivated?.Invoke(this, path);
                e.Handled = true;
            }
        }
    }

    internal static string? ResolveEnterActivationPath(string? focusedPath, ThumbItem? selectedItem)
    {
        return focusedPath ?? selectedItem?.Path;
    }

    private void OnListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            CtrlWheelZoom?.Invoke(e.Delta > 0 ? 1 : -1);
            return;
        }
    }

    private void OnThumbImageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        if (image.DataContext is not ThumbItem item)
        {
            return;
        }

        if (item.IsFolder)
        {
            image.Source = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _imageTokens[image] = cts;
        image.Source = null;

        var provider = ThumbnailProvider;
        if (provider is null)
        {
            return;
        }

        _ = LoadThumbnailAsync(image, item, cts.Token);
    }

    private async Task LoadThumbnailAsync(Image image, ThumbItem item, CancellationToken token)
    {
        try
        {
            if (item.IsFolder)
            {
                return;
            }

            var provider = ThumbnailProvider;
            if (provider is null)
            {
                return;
            }

            var bitmap = await provider(item.Path, TileSize, token);
            if (token.IsCancellationRequested || bitmap is null)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    image.Source = bitmap;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void OnThumbImageUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        if (_imageTokens.TryGetValue(image, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _imageTokens.Remove(image);
        }

        image.Source = null;
    }
}

public sealed class ThumbItem : INotifyPropertyChanged
{
    private string? _path;
    private string? _name;
    private string? _resolution;
    private string? _fileSize;

    public ThumbItem(string path, string name, bool isFolder = false)
    {
        Path = path;
        Name = name;
        IsFolder = isFolder;
    }

    public string Path
    {
        get => _path ?? string.Empty;
        set
        {
            if (_path == value)
            {
                return;
            }

            _path = value;
            OnPropertyChanged();
        }
    }

    public string Name
    {
        get => _name ?? string.Empty;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public bool IsFolder { get; set; }

    public string? Resolution
    {
        get => _resolution;
        set
        {
            if (_resolution == value)
            {
                return;
            }

            _resolution = value;
            OnPropertyChanged();
        }
    }

    public string? FileSize
    {
        get => _fileSize;
        set
        {
            if (_fileSize == value)
            {
                return;
            }

            _fileSize = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class TileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var add = 0;
        if (parameter is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            add = parsed;
        }
        else if (parameter is int i)
        {
            add = i;
        }

        var tileSize = value is int t ? t : 0;
        return tileSize + add;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
