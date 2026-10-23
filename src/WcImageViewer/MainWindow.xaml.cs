using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".cbz", ".7z", ".cb7", ".rar", ".cbr"
    };

    private readonly DispatcherTimer _animationTimer = new();
    private readonly DispatcherTimer _slideshowTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _thumbnailHideTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private readonly ImageDocumentCache _imageCache = new(3);
    private readonly HashSet<string> _preloading = new(StringComparer.Ordinal);
    private readonly List<int> _slideshowOrder = new();
    private readonly object _preloadingGate = new();

    private IImageSource? _source;
    private ImageDocument? _document;
    private int _index = -1;
    private int _frameIndex;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _preloadCts;
    private int _loadGeneration;
    private int _openGeneration;

    private bool _isFullscreen;
    private WindowState _oldState;
    private WindowStyle _oldStyle;
    private ResizeMode _oldResizeMode;
    private Rect _oldBounds;

    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private Dictionary<string, string> _shortcuts;
    private bool _thumbnailsVisible;
    private bool _thumbnailsHover;
    private bool _initialFocusSet;
    private bool _hasPositionedWindow;
    private bool _isWindowDragging;
    private double _windowDragOffsetX;
    private double _windowDragOffsetY;

    public MainWindow() : this(SettingsService.LoadDefault())
    {
    }

    public MainWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Settings;
        _shortcuts = ShortcutService.GetEffective(_settings.Shortcuts);

        InitializeComponent();
        Viewer.AllowUpscaleOnFit = !_settings.DoNotUpscaleSmallImages;
        ApplySettings();

        Viewer.ZoomChanged += (_, _) => UpdateZoomText();
        Viewer.NavigateRequested += (_, delta) => Navigate(delta);
        Viewer.WindowDragRequested += (_, _) => BeginWindowDrag();
        Viewer.LostMouseCapture += (_, _) => _isWindowDragging = false;
        Thumbnails.SelectionChanged += OnThumbnailSelected;
        _animationTimer.Tick += (_, _) => AdvanceAnimation();
        _slideshowTimer.Tick += (_, _) => AdvanceSlideshow();
        _noticeTimer.Tick += (_, _) => HideNavigationNotice();
        Loaded += (_, _) => Viewer.Focus();
        _thumbnailHideTimer.Tick += (_, _) =>
        {
            _thumbnailHideTimer.Stop();
            _thumbnailsHover = false;
            UpdateThumbnailVisibility();
        };
        ContentRendered += (_, _) =>
        {
            Viewer.Focus();
            Keyboard.Focus(Viewer);
        };
    }

    public void OpenPath(string path) => _ = OpenPathAsync(path);

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!_initialFocusSet)
        {
            _initialFocusSet = true;
            Viewer.Focus();
        }
    }

    private void ApplySettings()
    {
        Width = MinWidth;
        Height = MinHeight;
        WindowState = WindowState.Normal;

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var maxLeft = Math.Max(virtualLeft, virtualLeft + SystemParameters.VirtualScreenWidth - MinWidth);
        var maxTop = Math.Max(virtualTop, virtualTop + SystemParameters.VirtualScreenHeight - MinHeight);

        if (_settings.WindowLeft is double savedLeft &&
            _settings.WindowTop is double savedTop &&
            savedLeft >= virtualLeft - 50 &&
            savedLeft <= maxLeft + 50 &&
            savedTop >= virtualTop - 50 &&
            savedTop <= maxTop + 50)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Clamp(savedLeft, virtualLeft, maxLeft);
            Top = Math.Clamp(savedTop, virtualTop, maxTop);
            _hasPositionedWindow = true;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _thumbnailsVisible = _settings.ThumbnailsVisible;
        _thumbnailsHover = false;
        UpdateThumbnailVisibility();
        _slideshowTimer.Interval = TimeSpan.FromSeconds(
            Math.Clamp(_settings.SlideshowIntervalSeconds, 2, 60));
    }

    private async Task OpenPathAsync(string path)
    {
        var generation = ++_openGeneration;

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            path = Path.GetFullPath(path);

            if (Directory.Exists(path))
            {
                await OpenFolderAsync(path, generation);
                return;
            }

            if (!File.Exists(path))
            {
                ShowStatus("파일을 찾을 수 없습니다.");
                return;
            }

            if (ArchiveExtensions.Contains(Path.GetExtension(path)))
            {
                await OpenArchiveAsync(path, generation);
                return;
            }

            if (!ImageFileEnumerator.IsImage(path))
            {
                ShowStatus("지원하지 않는 이미지 형식입니다.");
                return;
            }

            await OpenFileAsync(path, generation);
        }
        catch (Exception ex) when (generation == _openGeneration)
        {
            ShowEmpty($"열 수 없습니다.\n{ex.Message}");
        }
    }

    private async Task OpenFolderAsync(string folder, int generation)
    {
        ShowStatus("폴더 여는 중...");
        var source = await Task.Run(() => FolderImageSource.Create(folder));

        if (generation != _openGeneration)
        {
            source.Dispose();
            return;
        }

        if (source.Count == 0)
        {
            source.Dispose();
            ShowEmpty("폴더에 이미지가 없습니다.");
            return;
        }

        await SetSourceAsync(source, 0);
        AddRecent(folder);
    }

    private async Task OpenFileAsync(string file, int generation)
    {
        var folder = Path.GetDirectoryName(file);
        var source = folder is null
            ? FolderImageSource.CreateForFile(file)
            : await Task.Run(() => FolderImageSource.Create(folder));

        if (generation != _openGeneration)
        {
            source.Dispose();
            return;
        }

        var index = folder is null ? 0 : source.IndexOf(file);
        if (index < 0)
        {
            source.Dispose();
            source = FolderImageSource.CreateForFile(file);
            index = 0;
        }

        await SetSourceAsync(source, index);
        AddRecent(file);
    }

    private async Task OpenArchiveAsync(string path, int generation)
    {
        ShowStatus("압축파일 여는 중...");

        var progress = new Progress<ArchiveProgress>(report =>
        {
            if (report.Total <= 0 || _document is not null)
            {
                return;
            }

            var name = string.IsNullOrEmpty(report.CurrentEntry)
                ? string.Empty
                : $"   {Path.GetFileName(report.CurrentEntry)}";
            ShowStatus($"압축 해제 중... {report.Completed}/{report.Total}{name}");
        });

        var source = await ArchiveImageSource.CreateAsync(path, progress, CancellationToken.None);

        if (generation != _openGeneration)
        {
            source.Dispose();
            return;
        }

        if (source.Count == 0)
        {
            source.Dispose();
            ShowEmpty("압축파일에 이미지가 없습니다.");
            return;
        }

        await SetSourceAsync(source, 0);
        AddRecent(path);
    }

    private async Task SetSourceAsync(IImageSource source, int index)
    {
        StopSlideshow();
        StopAnimation();
        _preloadCts?.Cancel();
        _preloadCts = new CancellationTokenSource();
        _imageCache.Clear();
        lock (_preloadingGate)
        {
            _preloading.Clear();
        }

        _source?.Dispose();

        _source = source;
        _index = Math.Clamp(index, 0, source.Count - 1);
        HideNavigationNotice();
        InfoOverlayText.Visibility = Visibility.Collapsed;
        _thumbnailsHover = false;
        UpdateThumbnailVisibility();
        Thumbnails.SetSource(source, _index);
        Title = source.DisplayName;

        await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        var source = _source;
        if (source is null || _index < 0 || _index >= source.Count)
        {
            return;
        }

        var generation = ++_loadGeneration;
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;

        StopAnimation();
        Thumbnails.SelectIndex(_index);

        var entry = source.GetEntry(_index);
        var cacheKey = CacheKey(source, entry);

        if (_imageCache.TryGet(cacheKey, out var cached) && cached is not null)
        {
            if (generation == _loadGeneration && ReferenceEquals(source, _source))
            {
                DisplayDocument(source, entry, cached);
            }

            return;
        }

        ShowStatus("불러오는 중...");

        try
        {
            using var stream = await source.OpenEntryAsync(_index, cancellationToken);
            var reopen = source is FolderImageSource
                ? (Func<Stream>)(() => ImageLoader.OpenFile(entry.Key))
                : null;
            var document = await Task.Run(
                () => ImageLoader.LoadDocument(stream, reopen, Path.GetExtension(entry.Name), cancellationToken),
                cancellationToken);

            if (generation != _loadGeneration || !ReferenceEquals(source, _source))
            {
                return;
            }

            _imageCache.Set(cacheKey, document);
            DisplayDocument(source, entry, document);
        }
        catch (OperationCanceledException)
        {
            // A newer navigation replaced this request.
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration || !ReferenceEquals(source, _source))
            {
                return;
            }

            StopAnimation();
            Viewer.Clear();
            InfoOverlayText.Visibility = Visibility.Collapsed;
            EmptyText.Text = $"이미지를 열 수 없습니다.\n{ex.Message}";
            EmptyText.Visibility = Visibility.Visible;
            Title = $"{entry.DisplayName} [{_index + 1}/{source.Count}] (오류)";
            UpdateZoomText();
        }
    }

    private void UpdateWindowTitle()
    {
        if (_source is null || _index < 0 || _index >= _source.Count)
        {
            Title = "WC Image Viewer";
            return;
        }

        var entry = _source.GetEntry(_index);
        var dimensions = _document is null
            ? string.Empty
            : $" ({_document.PixelWidth}x{_document.PixelHeight})";
        Title = $"{entry.DisplayName} [{_index + 1}/{_source.Count}]{dimensions}";
    }

    private void DisplayDocument(IImageSource source, ImageEntry entry, ImageDocument document)
    {
        StopAnimation();
        _document = document;
        Viewer.SetImage(document.Image, document.Tiles);

        if (!_isFullscreen && _settings.AutoFitWindowToImage)
        {
            FitWindowToImage();
        }
        else if (_settings.DefaultZoomPercent > 0)
        {
            Viewer.SetZoom(_settings.DefaultZoomPercent / 100.0);
        }

        EmptyText.Visibility = Visibility.Collapsed;
        UpdateWindowTitle();

        if (document.IsAnimated)
        {
            StartAnimation(document);
        }

        UpdateZoomText();

        if (InfoOverlayText.Visibility == Visibility.Visible)
        {
            UpdateInfoText();
        }

        StartPreload(source, _index);
    }

    private static string CacheKey(IImageSource source, ImageEntry entry)
    {
        return $"{source.Id}|{entry.Key}|{entry.Size}|{entry.LastModifiedUtc?.Ticks}";
    }

    private void StartPreload(IImageSource source, int index)
    {
        var token = _preloadCts?.Token ?? CancellationToken.None;
        _ = PreloadAsync(source, index - 1, token);
        _ = PreloadAsync(source, index + 1, token);
    }

    private async Task PreloadAsync(IImageSource source, int index, CancellationToken cancellationToken)
    {
        if (index < 0 || index >= source.Count || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var entry = source.GetEntry(index);
        var key = CacheKey(source, entry);
        if (_imageCache.TryGet(key, out _))
        {
            return;
        }

        lock (_preloadingGate)
        {
            if (!_preloading.Add(key))
            {
                return;
            }
        }

        try
        {
            using var stream = await source.OpenEntryAsync(index, cancellationToken);
            var reopen = source is FolderImageSource
                ? (Func<Stream>)(() => ImageLoader.OpenFile(entry.Key))
                : null;
            var document = await Task.Run(
                () => ImageLoader.LoadDocument(stream, reopen, Path.GetExtension(entry.Name), cancellationToken),
                cancellationToken);

            if (ReferenceEquals(source, _source) && !cancellationToken.IsCancellationRequested)
            {
                _imageCache.Set(key, document);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Preloading is best-effort.
        }
        finally
        {
            lock (_preloadingGate)
            {
                _preloading.Remove(key);
            }
        }
    }

    private void StartAnimation(ImageDocument document)
    {
        _document = document;
        _frameIndex = 0;
        _animationTimer.Stop();

        if (!document.IsAnimated)
        {
            return;
        }

        _animationTimer.Interval = GetDelay(document, 0);
        _animationTimer.Start();
    }

    private void AdvanceAnimation()
    {
        if (_document is null || !_document.IsAnimated)
        {
            _animationTimer.Stop();
            return;
        }

        _frameIndex = (_frameIndex + 1) % _document.FrameCount;

        BitmapSource frame;
        var started = Stopwatch.GetTimestamp();
        try
        {
            frame = _document.GetFrame(_frameIndex);
        }
        catch
        {
            _animationTimer.Stop();
            return;
        }

        Viewer.SetFrame(frame);

        var decodeTime = Stopwatch.GetElapsedTime(started);
        var interval = GetDelay(_document, _frameIndex) - decodeTime;
        _animationTimer.Interval = interval < TimeSpan.FromMilliseconds(1)
            ? TimeSpan.FromMilliseconds(1)
            : interval;

        if (InfoOverlayText.Visibility == Visibility.Visible)
        {
            UpdateInfoText();
        }
    }

    private static TimeSpan GetDelay(ImageDocument document, int index)
    {
        if (index < 0 || index >= document.Delays.Count)
        {
            return TimeSpan.FromMilliseconds(100);
        }

        var delay = document.Delays[index];
        return delay < TimeSpan.FromMilliseconds(20) || delay > TimeSpan.FromSeconds(10)
            ? TimeSpan.FromMilliseconds(100)
            : delay;
    }

    private void StopAnimation()
    {
        _animationTimer.Stop();
        _document = null;
        _frameIndex = 0;
    }

    private void OnThumbnailSelected(object? sender, int index)
    {
        if (_source is null || index < 0 || index >= _source.Count || index == _index)
        {
            return;
        }

        _index = index;
        _ = LoadCurrentAsync();
    }

    private void Navigate(int delta)
    {
        if (_source is null || _source.Count <= 1)
        {
            return;
        }

        var target = _index + delta;
        if (target < 0 || target >= _source.Count)
        {
            if (!_settings.WrapNavigation)
            {
                ShowNavigationNotice(target < 0 ? "첫 파일입니다." : "마지막 파일입니다.");
                return;
            }

            target = (target + _source.Count) % _source.Count;
            ShowNavigationNotice(delta > 0 ? "첫 파일입니다." : "마지막 파일입니다.");
        }

        _index = target;
        _ = LoadCurrentAsync();
    }

    private void ShowEmpty(string message)
    {
        StopSlideshow();
        StopAnimation();
        _loadCts?.Cancel();
        _preloadCts?.Cancel();
        _imageCache.Clear();
        lock (_preloadingGate)
        {
            _preloading.Clear();
        }

        _source?.Dispose();
        _source = null;
        _index = -1;

        HideNavigationNotice();
        Thumbnails.Clear();
        InfoOverlayText.Visibility = Visibility.Collapsed;
        _thumbnailsHover = false;
        UpdateThumbnailVisibility();
        Viewer.Clear();
        Title = "WC Image Viewer";
        EmptyText.Text = message;
        EmptyText.Visibility = Visibility.Visible;
        UpdateZoomText();
    }

    private void UpdateThumbnailVisibility()
    {
        var show = _source is not null && (_thumbnailsVisible || _thumbnailsHover);
        Thumbnails.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isWindowDragging)
        {
            MoveWindowToCursor();
            e.Handled = true;
            return;
        }

        if (_source is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var viewerBottom = GetViewerBottom();
        var inBottomTrigger = point.Y >= viewerBottom - 40;
        var overlayTop = GetThumbnailOverlayTop();
        var overOverlay = Thumbnails.Visibility == Visibility.Visible &&
                          point.Y >= overlayTop - 2;

        if (inBottomTrigger)
        {
            _thumbnailHideTimer.Stop();
            if (!_thumbnailsHover)
            {
                _thumbnailsHover = true;
                UpdateThumbnailVisibility();
            }
        }
        else if (_thumbnailsHover && overOverlay)
        {
            _thumbnailHideTimer.Stop();
        }
        else if (_thumbnailsHover && !_thumbnailHideTimer.IsEnabled)
        {
            _thumbnailHideTimer.Start();
        }
    }

    private double GetThumbnailOverlayTop()
    {
        var height = Thumbnails.ActualHeight > 0 ? Thumbnails.ActualHeight : 112;
        return GetViewerBottom() - height;
    }

    private double GetViewerBottom()
    {
        return Viewer.TranslatePoint(
            new Point(0, Math.Max(0, Viewer.ActualHeight)),
            this).Y;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _thumbnailHideTimer.Stop();
        _thumbnailsHover = false;
        UpdateThumbnailVisibility();
    }

    private void BeginWindowDrag()
    {
        if (_isWindowDragging)
        {
            return;
        }

        var cursor = GetCursorPositionInDips();
        _windowDragOffsetX = cursor.X - Left;
        _windowDragOffsetY = cursor.Y - Top;
        _isWindowDragging = true;
        Viewer.CaptureMouse();
    }

    private void MoveWindowToCursor()
    {
        var cursor = GetCursorPositionInDips();
        Left = cursor.X - _windowDragOffsetX;
        Top = cursor.Y - _windowDragOffsetY;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isWindowDragging)
        {
            return;
        }

        _isWindowDragging = false;
        Viewer.ReleaseMouseCapture();
        e.Handled = true;
    }

    private Point GetCursorPositionInDips()
    {
        if (!GetCursorPos(out var point))
        {
            return new Point(Left + Width / 2, Top + Height / 2);
        }

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return new Point(point.X, point.Y);
        }

        return source.CompositionTarget.TransformFromDevice.Transform(
            new Point(point.X, point.Y));
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointNative point);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    private void UpdateZoomText()
    {
        var text = Viewer.HasImage ? $"{Viewer.Zoom * 100:0}%" : string.Empty;
        ZoomBox.Text = text;
    }

    private void ApplyZoomText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        text = text.Trim();
        if (text.Equals("맞춤", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("fit", StringComparison.OrdinalIgnoreCase))
        {
            Viewer.FitToWindow();
            _settings.DefaultZoomPercent = 0;
            return;
        }

        text = text.TrimEnd('%').Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            UpdateZoomText();
            return;
        }

        if (percent <= 0)
        {
            Viewer.FitToWindow();
            _settings.DefaultZoomPercent = 0;
            return;
        }

        percent = Math.Clamp(percent, 5, 800);
        Viewer.SetZoom(percent / 100.0);
        _settings.DefaultZoomPercent = (int)Math.Round(percent);
    }

    private void OnZoomKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyZoomText(ZoomBox.Text);
            Viewer.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdateZoomText();
            Viewer.Focus();
            e.Handled = true;
        }
    }

    private void OnZoomLostFocus(object sender, RoutedEventArgs e)
    {
        ApplyZoomText(ZoomBox.Text);
    }

    private void FitWindowToImage()
    {
        if (_document is null)
        {
            return;
        }

        if (_isFullscreen)
        {
            ToggleFullscreen();
        }

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }

        UpdateLayout();

        var chromeWidth = Math.Max(0, ActualWidth - Viewer.ActualWidth);
        var chromeHeight = Math.Max(0, ActualHeight - Viewer.ActualHeight);
        var workArea = SystemParameters.WorkArea;

        var workViewportWidth = Math.Max(1, workArea.Width - chromeWidth);
        var workViewportHeight = Math.Max(1, workArea.Height - chromeHeight);
        var minViewportWidth = Math.Max(1, MinWidth - chromeWidth);
        var minViewportHeight = Math.Max(1, MinHeight - chromeHeight);

        var imageWidth = _document.PixelWidth;
        var imageHeight = _document.PixelHeight;

        double scale;
        if (imageWidth > workViewportWidth || imageHeight > workViewportHeight)
        {
            // Fit the whole image into the usable screen area.
            scale = Math.Min(
                workViewportWidth / imageWidth,
                workViewportHeight / imageHeight);
        }
        else if (imageWidth < minViewportWidth && imageHeight < minViewportHeight)
        {
            // Small images fill the minimum window instead of sitting tiny in it.
            scale = Math.Min(
                minViewportWidth / imageWidth,
                minViewportHeight / imageHeight);
        }
        else
        {
            scale = 1.0;
        }

        scale = Math.Clamp(scale, 0.01, 8.0);

        if (_settings.DoNotUpscaleSmallImages)
        {
            scale = Math.Min(scale, 1.0);
        }

        var displayedWidth = imageWidth * scale;
        var displayedHeight = imageHeight * scale;
        var targetWidth = Math.Clamp(displayedWidth + chromeWidth, MinWidth, workArea.Width);
        var targetHeight = Math.Clamp(displayedHeight + chromeHeight, MinHeight, workArea.Height);

        // Resize around the current window center so the image stays put on screen.
        var currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        var currentHeight = ActualHeight > 0 ? ActualHeight : Height;

        var targetLeft = !_hasPositionedWindow || double.IsNaN(Left)
            ? workArea.Left + (workArea.Width - targetWidth) / 2
            : Left + (currentWidth - targetWidth) / 2;
        var targetTop = !_hasPositionedWindow || double.IsNaN(Top)
            ? workArea.Top + (workArea.Height - targetHeight) / 2
            : Top + (currentHeight - targetHeight) / 2;
        _hasPositionedWindow = true;

        Width = targetWidth;
        Height = targetHeight;
        Left = Math.Clamp(targetLeft, workArea.Left, workArea.Right - targetWidth);
        Top = Math.Clamp(targetTop, workArea.Top, workArea.Bottom - targetHeight);

        Dispatcher.BeginInvoke(
            new Action(() => Viewer.SetZoom(scale)),
            DispatcherPriority.Loaded);
    }

    private bool TryExecuteShortcut(KeyEventArgs e)
    {
        foreach (var shortcut in _shortcuts)
        {
            if (ShortcutService.Matches(shortcut.Value, e))
            {
                ExecuteShortcut(shortcut.Key);
                return true;
            }
        }

        return false;
    }

    private void ExecuteShortcut(string actionId)
    {
        switch (actionId)
        {
            case "Open":
                ShowOpenFileDialog();
                break;
            case "CloseImage":
                CloseCurrentImage();
                break;
            case "Settings":
                OnSettingsClick(this, new RoutedEventArgs());
                break;
            case "Bookmarks":
                OpenBookmarks();
                break;
            case "Fullscreen":
                ToggleFullscreen();
                break;
            case "SaveAs":
                SaveCurrentAs();
                break;
            case "Previous":
                Navigate(-1);
                break;
            case "Next":
                Navigate(1);
                break;
            case "First":
                if (_source is { Count: > 0 })
                {
                    _index = 0;
                    _ = LoadCurrentAsync();
                }
                break;
            case "Last":
                if (_source is { Count: > 0 })
                {
                    _index = _source.Count - 1;
                    _ = LoadCurrentAsync();
                }
                break;
            case "ZoomIn":
                Viewer.ZoomIn();
                break;
            case "ZoomOut":
                Viewer.ZoomOut();
                break;
            case "Fit":
                Viewer.FitToWindow();
                _settings.DefaultZoomPercent = 0;
                break;
            case "ActualSize":
                Viewer.ActualSize();
                _settings.DefaultZoomPercent = 100;
                break;
            case "WindowFit":
                FitWindowToImage();
                break;
            case "Slideshow":
                ToggleSlideshow();
                break;
            case "Thumbnails":
                ToggleThumbnails();
                break;
            case "Info":
                ToggleInfoOverlay();
                break;
            case "Rename":
                _ = RenameCurrentAsync();
                break;
            case "Delete":
                _ = DeleteCurrentAsync();
                break;
            case "Copy":
                CopyCurrentImage();
                break;
            case "Edit":
                OpenInEditor();
                break;
            case "CloseWindow":
                Close();
                break;
        }
    }

    private void ToggleThumbnails()
    {
        if (_source is null)
        {
            return;
        }

        _thumbnailsVisible = !_thumbnailsVisible;
        _settings.ThumbnailsVisible = _thumbnailsVisible;
        if (!_thumbnailsVisible)
        {
            _thumbnailsHover = false;
        }

        UpdateThumbnailVisibility();
    }

    private void CloseCurrentImage()
    {
        if (_source is null)
        {
            return;
        }

        ShowEmpty("이미지를 열어주세요\n파일을 끌어다 놓아도 됩니다");
    }

    private void SaveCurrentAs()
    {
        if (_document is null)
        {
            return;
        }

        var entry = _source?.GetEntry(_index);
        var sourceName = entry?.DisplayName ?? "image";
        var suggestedName = Path.GetFileNameWithoutExtension(sourceName) + "_copy";

        var dialog = new SaveFileDialog
        {
            Title = "다른 이름으로 저장",
            FileName = suggestedName,
            DefaultExt = ".png",
            AddExtension = true,
            Filter = "PNG 이미지|*.png|JPEG 이미지|*.jpg;*.jpeg|BMP 이미지|*.bmp|TIFF 이미지|*.tif;*.tiff"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var frame = _document.IsAnimated &&
                    _frameIndex >= 0 &&
                    _frameIndex < _document.FrameCount
            ? _document.GetFrame(_frameIndex)
            : _document.Image;

        var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        BitmapEncoder encoder = extension switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };

        try
        {
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(
                this,
                $"이미지를 저장할 수 없습니다.\n{ex.Message}",
                "다른 이름으로 저장",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenBookmarks()
    {
        string? currentPath = _source is FolderImageSource &&
                              _index >= 0 &&
                              _index < _source.Count
            ? _source.GetEntry(_index).Key
            : null;

        var dialog = new BookmarkWindow(_settings.Bookmarks, currentPath)
        {
            Owner = this
        };

        dialog.ShowDialog();
        _settings.Bookmarks = dialog.Bookmarks.ToList();

        if (!string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OpenPath(dialog.SelectedPath);
        }
    }

    private void ShowStatus(string message)
    {
        Title = message;
    }

    private void ShowNavigationNotice(string message)
    {
        if (!_settings.ShowNavigationNotice)
        {
            return;
        }

        NoticeText.Text = message;
        NoticeBorder.BeginAnimation(OpacityProperty, null);
        NoticeBorder.Opacity = 1;
        NoticeBorder.Visibility = Visibility.Visible;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void HideNavigationNotice()
    {
        _noticeTimer.Stop();
        if (NoticeBorder.Visibility != Visibility.Visible)
        {
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (NoticeBorder.Opacity <= 0.01)
            {
                NoticeBorder.Visibility = Visibility.Collapsed;
            }
        };
        NoticeBorder.BeginAnimation(OpacityProperty, fade);
    }

    private void ToggleInfoOverlay()
    {
        if (InfoOverlayText.Visibility == Visibility.Visible)
        {
            InfoOverlayText.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateInfoText();
        InfoOverlayText.Visibility = Visibility.Visible;
    }

    private void UpdateInfoText()
    {
        if (_source is null || _index < 0 || _index >= _source.Count)
        {
            InfoOverlayText.Text = string.Empty;
            return;
        }

        var entry = _source.GetEntry(_index);
        var builder = new StringBuilder();
        builder.AppendLine(entry.DisplayName);

        if (_source is FolderImageSource)
        {
            builder.AppendLine(entry.Key);
        }
        else
        {
            builder.AppendLine($"{_source.DisplayName}  ›  {entry.Name}");
        }

        if (_document is not null)
        {
            builder.AppendLine($"{_document.PixelWidth} × {_document.PixelHeight} px");
            if (_document.DpiX > 0 && _document.DpiY > 0)
            {
                builder.AppendLine($"DPI  {_document.DpiX:0.#} × {_document.DpiY:0.#}");
            }

            if (_document.IsAnimated && _frameIndex >= 0 && _frameIndex < _document.FrameCount)
            {
                builder.AppendLine($"프레임  {_frameIndex + 1}/{_document.FrameCount}");
            }

            if (_document.IsTiled)
            {
                builder.AppendLine("표시  타일 디코딩 (원본 100%)");
            }
        }

        var extension = Path.GetExtension(entry.Name).TrimStart('.');
        if (!string.IsNullOrEmpty(extension))
        {
            builder.AppendLine($"형식  {extension.ToUpperInvariant()}");
        }

        if (entry.Size > 0)
        {
            builder.AppendLine($"크기  {FormatFileSize(entry.Size)}");
        }

        if (entry.LastModifiedUtc is DateTime modified)
        {
            builder.AppendLine($"수정  {modified.ToLocalTime():yyyy-MM-dd HH:mm}");
        }

        builder.Append($"{_index + 1} / {_source.Count}");
        InfoOverlayText.Text = builder.ToString();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1L << 30)
        {
            return $"{bytes / (double)(1L << 30):0.##} GB";
        }

        if (bytes >= 1L << 20)
        {
            return $"{bytes / (double)(1L << 20):0.##} MB";
        }

        if (bytes >= 1L << 10)
        {
            return $"{bytes / (double)(1L << 10):0.#} KB";
        }

        return $"{bytes} B";
    }

    private void CopyCurrentImage()
    {
        if (_document is null)
        {
            return;
        }

        var frame = _document.IsAnimated &&
                    _frameIndex >= 0 &&
                    _frameIndex < _document.FrameCount
            ? _document.GetFrame(_frameIndex)
            : _document.Image;

        try
        {
            Clipboard.SetImage(frame);
        }
        catch (ExternalException ex)
        {
            MessageBox.Show(
                this,
                $"이미지를 클립보드로 복사할 수 없습니다.\n{ex.Message}",
                "클립보드",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenInEditor()
    {
        if (_source is not FolderImageSource || _index < 0 || _index >= _source.Count)
        {
            ShowStatus("폴더에 있는 이미지 파일만 편집 앱으로 열 수 있습니다.");
            return;
        }

        var path = _source.GetEntry(_index).Key;
        if (!File.Exists(path))
        {
            ShowStatus("파일을 찾을 수 없습니다.");
            return;
        }

        var editor = _settings.EditorPath;
        if (string.IsNullOrWhiteSpace(editor) || !File.Exists(editor))
        {
            MessageBox.Show(
                this,
                "설정에서 편집 앱을 선택해 주세요.",
                "편집 앱",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = editor,
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            MessageBox.Show(
                this,
                $"편집 앱을 실행할 수 없습니다.\n{ex.Message}",
                "편집 앱",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task RenameCurrentAsync()
    {
        if (_source is not FolderImageSource || _index < 0 || _index >= _source.Count)
        {
            ShowStatus("폴더에 있는 이미지 파일만 이름을 바꿀 수 있습니다.");
            return;
        }

        var entry = _source.GetEntry(_index);
        var oldPath = entry.Key;
        var oldName = Path.GetFileName(oldPath);
        var dialog = new RenameWindow(oldName) { Owner = this };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var newName = dialog.NewName.Trim();
        if (string.Equals(newName, oldName, StringComparison.Ordinal))
        {
            return;
        }

        var directory = Path.GetDirectoryName(oldPath);
        if (directory is null)
        {
            return;
        }

        var newPath = Path.Combine(directory, newName);
        if (File.Exists(newPath))
        {
            MessageBox.Show(
                this,
                "같은 이름의 파일이 이미 있습니다.",
                "이름 바꾸기",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            File.Move(oldPath, newPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(
                this,
                $"이름을 바꿀 수 없습니다.\n{ex.Message}",
                "이름 바꾸기",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        await ReloadFolderAsync(directory, _index, newPath);
        if (_settings.RecentPaths.RemoveAll(
                item => string.Equals(item, oldPath, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            AddRecent(newPath);
        }
    }

    private async Task DeleteCurrentAsync()
    {
        if (_source is not FolderImageSource || _index < 0 || _index >= _source.Count)
        {
            ShowStatus("폴더에 있는 이미지 파일만 삭제할 수 있습니다.");
            return;
        }

        var entry = _source.GetEntry(_index);
        var path = entry.Key;
        var name = entry.DisplayName;
        var useRecycleBin = _settings.UseRecycleBin;
        var ask = _settings.ConfirmDelete || !useRecycleBin;

        if (ask)
        {
            var action = useRecycleBin ? "휴지통으로 이동" : "영구 삭제";
            var message = $"'{name}' 파일을 {action}할까요?";
            if (!useRecycleBin)
            {
                message += "\n\n영구 삭제는 되돌릴 수 없습니다.";
            }

            var answer = MessageBox.Show(
                this,
                message,
                "파일 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        StopSlideshow();
        StopAnimation();

        try
        {
            FileDeletionService.Delete(path, useRecycleBin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(
                this,
                $"파일을 삭제할 수 없습니다.\n{ex.Message}",
                "파일 삭제",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _settings.RecentPaths.RemoveAll(
            item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));

        var directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            ShowEmpty("이미지가 없습니다.");
            return;
        }

        await ReloadFolderAsync(directory, _index, null);
    }

    private async Task ReloadFolderAsync(string folder, int oldIndex, string? preferredPath)
    {
        var source = await Task.Run(() => FolderImageSource.Create(folder));

        if (source.Count == 0)
        {
            source.Dispose();
            ShowEmpty("폴더에 이미지가 없습니다.");
            return;
        }

        var index = preferredPath is null ? -1 : source.IndexOf(preferredPath);
        if (index < 0)
        {
            index = Math.Min(oldIndex, source.Count - 1);
        }

        await SetSourceAsync(source, index);
    }

    private void AddRecent(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        _settings.RecentPaths.RemoveAll(
            item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentPaths.Insert(0, path);

        while (_settings.RecentPaths.Count > 10)
        {
            _settings.RecentPaths.RemoveAt(_settings.RecentPaths.Count - 1);
        }
    }

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        if (_settings.RecentPaths.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "최근 항목 없음", IsEnabled = false });
        }
        else
        {
            foreach (var path in _settings.RecentPaths)
            {
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                var item = new MenuItem
                {
                    Header = string.IsNullOrEmpty(name) ? path : name,
                    ToolTip = path
                };
                item.Click += (_, _) => OpenPath(path);
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "최근 기록 지우기" };
            clear.Click += (_, _) => _settings.RecentPaths.Clear();
            menu.Items.Add(clear);
        }

        menu.PlacementTarget = RecentButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(
            _settings.SlideshowIntervalSeconds,
            _settings.SlideshowRepeat,
            _settings.SlideshowRandom,
            _thumbnailsVisible,
            _settings.DefaultZoomPercent,
            _settings.AutoFitWindowToImage,
            _settings.DoNotUpscaleSmallImages,
            _settings.ConfirmDelete,
            _settings.UseRecycleBin,
            _settings.EditorPath,
            _settings.AssociatedExtensions,
            _settings.SingleWindowMode,
            _settings.WrapNavigation,
            _settings.ShowNavigationNotice,
            _settings.Shortcuts)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _settings.SlideshowIntervalSeconds = Math.Clamp(dialog.IntervalSeconds, 2, 60);
        _slideshowTimer.Interval = TimeSpan.FromSeconds(_settings.SlideshowIntervalSeconds);
        _settings.SlideshowRepeat = dialog.SlideshowRepeat;
        _settings.SlideshowRandom = dialog.SlideshowRandom;
        if (_slideshowTimer.IsEnabled && _settings.SlideshowRandom)
        {
            ResetSlideshowOrder();
        }

        _settings.ThumbnailsVisible = dialog.ShowThumbnails;
        _thumbnailsVisible = dialog.ShowThumbnails;
        _settings.DefaultZoomPercent = dialog.DefaultZoomPercent;
        _settings.AutoFitWindowToImage = dialog.AutoFitWindowToImage;
        _settings.DoNotUpscaleSmallImages = dialog.DoNotUpscaleSmallImages;
        Viewer.AllowUpscaleOnFit = !_settings.DoNotUpscaleSmallImages;
        _settings.ConfirmDelete = dialog.ConfirmDelete;
        _settings.UseRecycleBin = dialog.UseRecycleBin;
        _settings.EditorPath = dialog.EditorPath;
        _settings.SingleWindowMode = dialog.SingleWindowMode;
        _settings.WrapNavigation = dialog.WrapNavigation;
        _settings.ShowNavigationNotice = dialog.ShowNavigationNotice;
        if (!_settings.ShowNavigationNotice)
        {
            HideNavigationNotice();
        }

        _settings.AssociatedExtensions = dialog.SelectedExtensions.ToList();
        _settings.AssociateFileTypes = _settings.AssociatedExtensions.Count > 0;
        _settings.Shortcuts = dialog.SelectedShortcuts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        _shortcuts = ShortcutService.GetEffective(_settings.Shortcuts);

        try
        {
            var executablePath = FileAssociationService.GetExecutablePath();
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new IOException("앱 실행 경로를 찾을 수 없습니다.");
            }

            FileAssociationService.Apply(_settings.AssociatedExtensions, executablePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"파일 연결을 변경할 수 없습니다.\n{ex.Message}",
                "파일 연결",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _thumbnailsHover = false;
        UpdateThumbnailVisibility();

        if (_document is not null)
        {
            if (_settings.AutoFitWindowToImage)
            {
                FitWindowToImage();
            }
            else if (_settings.DefaultZoomPercent > 0)
            {
                Viewer.SetZoom(_settings.DefaultZoomPercent / 100.0);
            }
            else
            {
                Viewer.FitToWindow();
            }
        }
    }

    private void CaptureSettings()
    {
        _settings.ThumbnailsVisible = _thumbnailsVisible;

        if (_isFullscreen)
        {
            _settings.IsMaximized = _oldState == WindowState.Maximized;
            _settings.WindowLeft = _oldBounds.Left;
            _settings.WindowTop = _oldBounds.Top;
            _settings.WindowWidth = _oldBounds.Width;
            _settings.WindowHeight = _oldBounds.Height;
        }
        else
        {
            _settings.IsMaximized = WindowState == WindowState.Maximized;
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            if (bounds.Width >= 200 && bounds.Height >= 150)
            {
                _settings.WindowLeft = bounds.Left;
                _settings.WindowTop = bounds.Top;
                _settings.WindowWidth = bounds.Width;
                _settings.WindowHeight = bounds.Height;
            }
        }

        try
        {
            _settingsService.Save();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e) => ShowOpenFileDialog();

    private void ShowOpenFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "이미지 또는 압축파일 열기",
            Filter = "이미지/압축 파일|*.jpg;*.jpeg;*.jpe;*.png;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.webp;*.avif;*.heic;*.heif;*.zip;*.cbz;*.7z;*.cb7;*.rar;*.cbr|모든 파일|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            OpenPath(dialog.FileName);
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "이미지 폴더 열기"
        };

        if (dialog.ShowDialog(this) == true)
        {
            OpenPath(dialog.FolderName);
        }
    }

    private void ToggleSlideshow()
    {
        if (_source is null || _source.Count == 0)
        {
            return;
        }

        if (_slideshowTimer.IsEnabled)
        {
            StopSlideshow();
        }
        else
        {
            if (_settings.SlideshowRandom)
            {
                ResetSlideshowOrder();
            }

            _slideshowTimer.Start();
            SlideshowButton.Content = "중지";
        }
    }

    private void AdvanceSlideshow()
    {
        if (_source is null || _source.Count == 0)
        {
            StopSlideshow();
            return;
        }

        if (_settings.SlideshowRandom)
        {
            if (_slideshowOrder.Count == 0)
            {
                if (!_settings.SlideshowRepeat)
                {
                    StopSlideshow();
                    return;
                }

                ResetSlideshowOrder();
                if (_slideshowOrder.Count == 0)
                {
                    // Single-image folder: keep the current image on screen.
                    return;
                }
            }

            _index = _slideshowOrder[0];
            _slideshowOrder.RemoveAt(0);
            _ = LoadCurrentAsync();
            return;
        }

        if (_index >= _source.Count - 1)
        {
            if (!_settings.SlideshowRepeat)
            {
                StopSlideshow();
                return;
            }

            _index = 0;
        }
        else
        {
            _index++;
        }

        _ = LoadCurrentAsync();
    }

    private void ResetSlideshowOrder()
    {
        _slideshowOrder.Clear();
        if (_source is null || _source.Count == 0)
        {
            return;
        }

        for (var index = 0; index < _source.Count; index++)
        {
            if (index != _index)
            {
                _slideshowOrder.Add(index);
            }
        }

        for (var index = _slideshowOrder.Count - 1; index > 0; index--)
        {
            var swap = Random.Shared.Next(index + 1);
            (_slideshowOrder[index], _slideshowOrder[swap]) =
                (_slideshowOrder[swap], _slideshowOrder[index]);
        }
    }

    private void StopSlideshow()
    {
        _slideshowTimer.Stop();
        _slideshowOrder.Clear();
        SlideshowButton.Content = "슬라이드";
    }

    private void OnSlideshowClick(object sender, RoutedEventArgs e) => ToggleSlideshow();

    private void OnPrevClick(object sender, RoutedEventArgs e) => Navigate(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => Navigate(1);

    private void OnFitClick(object sender, RoutedEventArgs e)
    {
        Viewer.FitToWindow();
        _settings.DefaultZoomPercent = 0;
    }

    private void OnActualSizeClick(object sender, RoutedEventArgs e)
    {
        Viewer.ActualSize();
        _settings.DefaultZoomPercent = 100;
    }

    private void OnFitWindowClick(object sender, RoutedEventArgs e) => FitWindowToImage();

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            OpenPath(paths[0]);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (TryExecuteShortcut(e))
        {
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _oldState = WindowState;
            _oldStyle = WindowStyle;
            _oldResizeMode = ResizeMode;
            _oldBounds = new Rect(Left, Top, Width, Height);

            ToolbarBorder.Visibility = Visibility.Collapsed;
            _thumbnailsHover = false;
            UpdateThumbnailVisibility();
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            _isFullscreen = true;
        }
        else
        {
            _isFullscreen = false;
            WindowStyle = _oldStyle;
            ResizeMode = _oldResizeMode;
            WindowState = _oldState;
            Left = _oldBounds.Left;
            Top = _oldBounds.Top;
            Width = _oldBounds.Width;
            Height = _oldBounds.Height;
            ToolbarBorder.Visibility = Visibility.Visible;
            UpdateThumbnailVisibility();
        }

        Viewer.FitToWindow();
    }

    protected override void OnClosed(EventArgs e)
    {
        CaptureSettings();
        _slideshowTimer.Stop();
        _animationTimer.Stop();
        _thumbnailHideTimer.Stop();
        _noticeTimer.Stop();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _preloadCts?.Cancel();
        _preloadCts?.Dispose();
        _source?.Dispose();
        base.OnClosed(e);
    }
}
