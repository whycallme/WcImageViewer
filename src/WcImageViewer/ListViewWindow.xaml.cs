using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WcImageViewer.Controls;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class ListViewWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly SettingsService? _settingsService;
    private readonly Dictionary<string, string> _shortcuts;
    private readonly string _startFolder;
    private string? _startFile;
    private readonly ListThumbnailLoader _loader = new();

    private CancellationTokenSource? _folderCts;
    private CancellationTokenSource? _previewCts;
    private DispatcherTimer? _previewTimer;
    private string? _pendingPreviewPath;
    private DispatcherTimer? _layoutSaveTimer;
    private DispatcherTimer? _previewAreaTimer;
    private string? _currentPreviewPath;
    private int _currentPreviewDecodeWidth;
    private readonly Dictionary<string, BitmapSource> _previewCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _previewCacheOrder = new();

    private ImageDocument? _previewDocument;
    private int _previewFrameIndex;
    private DispatcherTimer? _previewAnimTimer;

    private string _currentFolder = string.Empty;
    private int _itemCount;
    private readonly List<ListSorter.ImageEntry> _currentEntries = new();
    private ListViewPrefs _currentPrefs = new();
    private bool _applyingUI;
    private DispatcherTimer? _prefsSaveTimer;
    private readonly List<string> _backStack = new();
    private readonly List<string> _forwardStack = new();
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromMilliseconds(1200) };

    public string? OpenInViewerPath { get; private set; }

    public ListViewWindow(AppSettings settings, Action saveSettings, string startFolder, string? startFile, SettingsService? settingsService = null)
    {
        InitializeComponent();
        sizeSlider.ValueChanged += SizeSlider_ValueChanged;
        sortCombo.SelectionChanged += SortCombo_SelectionChanged;
        sortDescToggle.Checked += SortDescToggle_Changed;
        sortDescToggle.Unchecked += SortDescToggle_Changed;
        SourceInitialized += (_, _) => WindowCornerService.Apply(this);

        _settings = settings;
        _saveSettings = saveSettings;
        _settingsService = settingsService;
        _shortcuts = ShortcutService.GetEffective(_settings.Shortcuts);
        _startFolder = startFolder;
        _startFile = startFile;

        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            NoticeBorder.Visibility = Visibility.Collapsed;
        };
        PreviewMouseDown += OnWindowPreviewMouseDown;

        _prefsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _prefsSaveTimer.Tick += (_, _) =>
        {
            _prefsSaveTimer?.Stop();
            SavePrefsNow();
        };

        _layoutSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _layoutSaveTimer.Tick += (_, _) =>
        {
            _layoutSaveTimer?.Stop();
            SaveLayoutNow();
        };

        _previewAreaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _previewAreaTimer.Tick += (_, _) =>
        {
            _previewAreaTimer?.Stop();
            _ = RefreshPreviewForAreaAsync();
        };

        _previewAnimTimer = new DispatcherTimer();
        _previewAnimTimer.Tick += (_, _) => AdvancePreviewAnimation();

        folderTree.Initialize(startFolder);
        folderTree.FolderSelected += (_, folder) => _ = LoadFolder(folder);
        UpdateNavigationButtons();

        thumbList.ThumbnailProvider = _loader.GetAsync;
        thumbList.ItemFontSize = _settings.ListViewItemFontSize;
        thumbList.SelectionChanged += (_, _) =>
        {
            UpdateInfoPanel();
            UpdateActionButtons();
            if (thumbList.SelectedPaths.Count == 0)
            {
                SchedulePreview(null);
                return;
            }
            var path = thumbList.LastClickedPath ?? thumbList.FocusedPath;
            if (path is null && thumbList.SelectedPaths.Count > 0)
            {
                path = thumbList.SelectedPaths[thumbList.SelectedPaths.Count - 1];
            }
            SchedulePreview(path);
        };
        thumbList.ItemActivated += (_, path) =>
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var attr = File.GetAttributes(path);
                    if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        _ = LoadFolder(path);
                        return;
                    }
                }
            }
            catch
            {
            }
            OpenInViewerPath = path;
            Close();
        };
        thumbList.EmptyAreaDoubleClicked += (_, _) => UpButton_Click(this, new RoutedEventArgs());
        thumbList.CtrlWheelZoom += delta =>
        {
            if (!IsInitialized || _applyingUI || sizeSlider is null)
            {
                return;
            }
            var v = sizeSlider.Value + delta * 16;
            if (v < sizeSlider.Minimum) v = sizeSlider.Minimum;
            if (v > sizeSlider.Maximum) v = sizeSlider.Maximum;
            sizeSlider.Value = v;
        };

        previewHost.SizeChanged += (_, _) =>
        {
            _previewAreaTimer?.Stop();
            _previewAreaTimer?.Start();
        };

        SizeChanged += (_, _) => ScheduleLayoutSave();
        LocationChanged += (_, _) => ScheduleLayoutSave();

        Closing += (_, _) =>
        {
            _layoutSaveTimer?.Stop();
            SaveLayoutNow();
        };

        Closed += (_, _) =>
        {
            _prefsSaveTimer?.Stop();
            _layoutSaveTimer?.Stop();
            _previewAreaTimer?.Stop();
            _previewAnimTimer?.Stop();
            if (_previewDocument is not null)
            {
                _previewDocument.Dispose();
                _previewDocument = null;
            }
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _folderCts?.Cancel();
            _folderCts?.Dispose();

            try { if (_settingsService != null) { _settingsService.Save(); } else { _saveSettings(); } } catch { }
            _ = PromptUpdateOnCloseAsync();
        };

        RestoreLayout();

        Loaded += OnLoaded;
    }

    private async Task PromptUpdateOnCloseAsync()
    {
        UpdateCheckResult result;
        try
        {
            result = await UpdateService.Instance.CheckAsync(AppVersion.Current, manual: false);
        }
        catch
        {
            return;
        }

        if (result.Status != UpdateCheckStatus.Available || result.Release == null)
        {
            return;
        }

        var main = Application.Current.MainWindow as MainWindow;
        if (main == null)
        {
            return;
        }

        main.Show();
        main.Activate();
        main.CompleteUpdatePrompt(result.Release);
    }

    private void RestoreLayout()
    {
        try
        {
            var layout = _settings.ListViewLayout ?? new ListViewLayout();

            if (layout.WindowWidth is double w && w > 0)
            {
                Width = w;
            }
            if (layout.WindowHeight is double h && h > 0)
            {
                Height = h;
            }

            if (layout.LeftColumnWidth > 0 && contentGrid is not null)
            {
                contentGrid.ColumnDefinitions[0].Width = new GridLength(layout.LeftColumnWidth);
            }
            if (leftGrid is not null)
            {
                var ratio = Math.Clamp(layout.LeftTopHeightRatio, 0.1, 0.9);
                leftGrid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
                leftGrid.RowDefinitions[2].Height = new GridLength(1.0 - ratio, GridUnitType.Star);
            }
            if (rightGrid is not null)
            {
                rightGrid.RowDefinitions[2].Height = new GridLength(Math.Clamp(layout.InfoHeight, 80, 500));
            }

            if (layout.WindowLeft is double wl && layout.WindowTop is double wt)
            {
                var vsLeft = SystemParameters.VirtualScreenLeft;
                var vsTop = SystemParameters.VirtualScreenTop;
                var vsWidth = SystemParameters.VirtualScreenWidth;
                var vsHeight = SystemParameters.VirtualScreenHeight;
                if (wl >= vsLeft && wt >= vsTop &&
                    wl + Width <= vsLeft + vsWidth && wt + Height <= vsTop + vsHeight)
                {
                    Left = wl;
                    Top = wt;
                }
                else
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }

            if (layout.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch
        {
        }
    }

    private void SaveLayoutNow()
    {
        try
        {
            var layout = _settings.ListViewLayout ??= new ListViewLayout();

            if (WindowState == WindowState.Normal)
            {
                layout.WindowLeft = Left;
                layout.WindowTop = Top;
                layout.WindowWidth = Width;
                layout.WindowHeight = Height;
            }
            else
            {
                layout.WindowLeft = RestoreBounds.Left;
                layout.WindowTop = RestoreBounds.Top;
                layout.WindowWidth = RestoreBounds.Width;
                layout.WindowHeight = RestoreBounds.Height;
            }
            layout.Maximized = WindowState == WindowState.Maximized;

            if (contentGrid is not null)
            {
                var col = contentGrid.ColumnDefinitions[0];
                var actual = col.ActualWidth;
                if (actual > 0)
                {
                    layout.LeftColumnWidth = actual;
                }
            }
            if (leftGrid is not null)
            {
                var topH = leftGrid.RowDefinitions[0].ActualHeight;
                var botH = leftGrid.RowDefinitions[2].ActualHeight;
                if (topH > 0 && botH > 0)
                {
                    layout.LeftTopHeightRatio = topH / (topH + botH);
                }
            }
            if (rightGrid is not null)
            {
                var h = rightGrid.RowDefinitions[2].ActualHeight;
                if (h > 0)
                {
                    layout.InfoHeight = h;
                }
            }

            _settings.ListViewLayout = layout;
            _saveSettings();
        }
        catch
        {
        }
    }

    private void ScheduleLayoutSave()
    {
        if (_layoutSaveTimer is null)
        {
            _layoutSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _layoutSaveTimer.Tick += (_, _) =>
            {
                _layoutSaveTimer?.Stop();
                SaveLayoutNow();
            };
        }
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Start();
    }

    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        SaveLayoutNow();
    }

    internal static int ComputePreviewDecodeWidth(double w, double h, double dpiScale)
    {
        var max = Math.Max(w, h);
        var scaled = max * dpiScale;
        var units = Math.Ceiling(scaled / 256.0);
        var width = (int)(units * 256.0);
        return Math.Clamp(width, 256, 4096);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadFolder(_startFolder);
            if (_startFile is not null)
            {
                var file = _startFile;
                _startFile = null;
                // 항목이 채워지고 레이아웃이 끝난 뒤(ContextIdle) 선택·스크롤·포커스를 준다.
                _ = Dispatcher.BeginInvoke(new Action(() => thumbList?.SelectPath(file)), DispatcherPriority.ContextIdle);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "리스트뷰 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task LoadFolder(string folder, bool fromHistory = false)
    {
        try
        {
            StopPreviewAnimation();
            _currentPreviewPath = null;
            _previewCts?.Cancel();

            if (!fromHistory && !string.IsNullOrEmpty(_currentFolder))
            {
                if (!string.Equals(_currentFolder, folder, StringComparison.OrdinalIgnoreCase))
                {
                    _backStack.Add(_currentFolder);
                    _forwardStack.Clear();
                    UpdateNavigationButtons();
                }
            }

        _folderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _folderCts = cts;
        var token = cts.Token;

        ListViewPrefs prefs;
        try
        {
            prefs = ListViewPrefsResolver.Get(_settings, folder);
        }
        catch
        {
            prefs = new ListViewPrefs();
        }

        List<ListSorter.ImageEntry> entries = new();
        IReadOnlyList<ThumbItem> items = Array.Empty<ThumbItem>();
        try
        {
            var result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var folderItems = new List<ThumbItem>();
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(folder))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 || (dirInfo.Attributes & FileAttributes.System) != 0)
                            {
                                continue;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                        folderItems.Add(new ThumbItem(dir, Path.GetFileName(dir) ?? dir, true));
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch
                {
                }
                folderItems.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                var paths = ImageFileEnumerator.Enumerate(folder);
                var ents = paths.Select(ListSorter.FromFile).ToList();
                var sorted = ListSorter.Sort(ents, prefs.SortKey, prefs.SortDescending);
                var fileItems = sorted.Select(e => new ThumbItem(e.Path, e.Name)).ToList();
                var thumbItems = new List<ThumbItem>(folderItems.Count + fileItems.Count);
                thumbItems.AddRange(folderItems);
                thumbItems.AddRange(fileItems);
                return (Entries: sorted.ToList(), Items: (IReadOnlyList<ThumbItem>)thumbItems);
            }, token);

            if (token.IsCancellationRequested)
            {
                return;
            }
            entries = result.Entries;
            items = result.Items;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (token.IsCancellationRequested || !ReferenceEquals(cts, _folderCts))
        {
            return;
        }

        _currentEntries.Clear();
        _currentEntries.AddRange(entries);
        _currentPrefs = prefs;

        _applyingUI = true;
        sizeSlider.Value = prefs.ThumbSize;
        sortCombo.SelectedIndex = SortKeyToIndex(prefs.SortKey);
        sortDescToggle.IsChecked = prefs.SortDescending;
        _applyingUI = false;

        thumbList.TileSize = prefs.ThumbSize;
        sizeValueText.Text = prefs.ThumbSize.ToString();
        thumbList.SetItems(items);

        _ = LoadThumbnailResolutionsAsync(items, token);

        _currentFolder = folder;
        _itemCount = items.Count;
        if (addressBox != null)
        {
            addressBox.Text = _currentFolder;
        }
        UpdateNavigationButtons();
        UpdateInfoPanel();
        UpdateActionButtons();
        _ = folderTree.RevealFolderAsync(folder);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "리스트뷰 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadThumbnailResolutionsAsync(IReadOnlyList<ThumbItem> items, CancellationToken token)
    {
        if (items.Count == 0)
        {
            return;
        }

        using var semaphore = new SemaphoreSlim(4);
        var tasks = new List<Task>(items.Count);

        foreach (var item in items)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    if (item.IsFolder)
                    {
                        return;
                    }

                    string? resolution = null;
                    long sizeBytes = -1;
                    try { sizeBytes = new FileInfo(item.Path).Length; } catch { }
                    try
                    {
                        using var stream = File.OpenRead(item.Path);
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                        if (decoder.Frames.Count > 0)
                        {
                            var frame = decoder.Frames[0];
                            resolution = $"{frame.PixelWidth}x{frame.PixelHeight}";
                        }
                    }
                    catch
                    {
                        // 예외는 무시
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        item.Resolution = resolution ?? string.Empty;
                        item.FileSize = sizeBytes >= 0 ? FileSizeFormatter.Format(sizeBytes) : string.Empty;
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
                finally
                {
                    semaphore.Release();
                }
            }, token));
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void SchedulePreview(string? path)
    {
        _pendingPreviewPath = path;
        if (_previewTimer is null)
        {
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _previewTimer.Tick += PreviewTimer_Tick;
        }
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        _previewTimer?.Stop();
        StopPreviewAnimation();
        var path = _pendingPreviewPath;

        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        var token = cts.Token;

        if (string.IsNullOrWhiteSpace(path))
        {
            _currentPreviewPath = null;
            _currentPreviewDecodeWidth = 0;
            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                previewImage.Source = null;
                previewStatus.Text = string.Empty;
                previewStatus.Visibility = Visibility.Collapsed;
            });
            return;
        }

        _currentPreviewPath = path;
        var decodeWidth = ComputePreviewDecodeWidth(
            previewHost.ActualWidth > 0 ? previewHost.ActualWidth : previewImage.ActualWidth,
            previewHost.ActualHeight > 0 ? previewHost.ActualHeight : previewImage.ActualHeight,
            1.0); // DPI scale unknown here; VisualTreeHelper? use 1.0 default
        // Try to get DPI scale from presentation source
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source is not null && source.CompositionTarget is not null)
            {
                decodeWidth = ComputePreviewDecodeWidth(
                    previewHost.ActualWidth > 0 ? previewHost.ActualWidth : previewImage.ActualWidth,
                    previewHost.ActualHeight > 0 ? previewHost.ActualHeight : previewImage.ActualHeight,
                    source.CompositionTarget.TransformToDevice.M11);
            }
        }
        catch
        {
        }

        await LoadPreviewAsync(path, decodeWidth, cts, token);
    }

    private async Task LoadPreviewAsync(string path, int decodeWidth, CancellationTokenSource cts, CancellationToken token)
    {
        var cacheKey = $"{path}|{decodeWidth}";
        if (_previewCache.TryGetValue(cacheKey, out var cached))
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || !ReferenceEquals(cts, _previewCts))
                {
                    return;
                }
                previewImage.Source = cached;
                previewStatus.Visibility = Visibility.Collapsed;
            });
            if (!token.IsCancellationRequested && ReferenceEquals(cts, _previewCts))
            {
                _currentPreviewDecodeWidth = decodeWidth;
            }
            return;
        }

        var ext = Path.GetExtension(path);
        var maybeAnimated = string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase);
        if (maybeAnimated)
        {
            ImageDocument? document = null;
            try
            {
                document = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return ImageLoader.LoadDocument(path, token);
                }, token);
            }
            catch (OperationCanceledException)
            {
                document?.Dispose();
                return;
            }
            catch
            {
                document?.Dispose();
                document = null;
            }

            if (token.IsCancellationRequested || !ReferenceEquals(cts, _previewCts))
            {
                document?.Dispose();
                return;
            }

            if (document is not null && document.IsAnimated)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || !ReferenceEquals(cts, _previewCts))
                    {
                        document?.Dispose();
                        return;
                    }
                    StopPreviewAnimation();
                    _previewDocument = document;
                    document = null;
                    StartPreviewAnimation(_previewDocument);
                    previewStatus.Visibility = Visibility.Collapsed;
                });

                if (token.IsCancellationRequested || !ReferenceEquals(cts, _previewCts))
                {
                    await Dispatcher.InvokeAsync(() => StopPreviewAnimation());
                    document?.Dispose();
                    return;
                }
                _currentPreviewDecodeWidth = decodeWidth;
                return;
            }

            document?.Dispose();
        }

        previewStatus.Visibility = Visibility.Collapsed;
        BitmapSource? bitmap = null;
        try
        {
            bitmap = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(path);
                var result = ImageLoader.LoadThumbnail(stream, decodeWidth, token);
                if (result is BitmapSource bs && !bs.IsFrozen)
                {
                    bs.Freeze();
                }
                return result;
            }, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            if (token.IsCancellationRequested || !ReferenceEquals(cts, _previewCts))
            {
                return;
            }
            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || !ReferenceEquals(cts, _previewCts))
                {
                    return;
                }
                previewImage.Source = null;
                previewStatus.Text = "미리보기 불가";
                previewStatus.Visibility = Visibility.Visible;
            });
            return;
        }

        if (token.IsCancellationRequested || !ReferenceEquals(cts, _previewCts))
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested || !ReferenceEquals(cts, _previewCts))
            {
                return;
            }
            if (bitmap is null)
            {
                previewImage.Source = null;
                previewStatus.Text = "미리보기 불가";
                previewStatus.Visibility = Visibility.Visible;
            }
            else
            {
                previewImage.Source = bitmap;
                previewStatus.Visibility = Visibility.Collapsed;
                CachePreview(cacheKey, bitmap);
                _currentPreviewDecodeWidth = decodeWidth;
            }
        });
    }

    private void CachePreview(string key, BitmapSource bitmap)
    {
        if (_previewCache.ContainsKey(key))
        {
            return;
        }
        _previewCache[key] = bitmap;
        _previewCacheOrder.Enqueue(key);
        while (_previewCacheOrder.Count > 3)
        {
            var old = _previewCacheOrder.Dequeue();
            _previewCache.Remove(old);
        }
    }

    private void StopPreviewAnimation()
    {
        _previewAnimTimer?.Stop();
        if (_previewDocument is not null)
        {
            _previewDocument.Dispose();
            _previewDocument = null;
        }
        _previewFrameIndex = 0;
        previewImage.Source = null;
    }

    private void StartPreviewAnimation(ImageDocument document)
    {
        _previewDocument = document;
        _previewFrameIndex = 0;

        if (!document.IsAnimated)
        {
            previewImage.Source = document.Image;
            return;
        }

        if (_previewAnimTimer is null)
        {
            _previewAnimTimer = new DispatcherTimer();
            _previewAnimTimer.Tick += (_, _) => AdvancePreviewAnimation();
        }

        _previewAnimTimer.Stop();
        previewImage.Source = document.GetFrame(0);
        _previewAnimTimer.Interval = GetPreviewDelay(document, 0);
        _previewAnimTimer.Start();
    }

    private void AdvancePreviewAnimation()
    {
        if (_previewDocument is null || !_previewDocument.IsAnimated)
        {
            _previewAnimTimer?.Stop();
            return;
        }

        _previewFrameIndex = (_previewFrameIndex + 1) % _previewDocument.FrameCount;

        BitmapSource frame;
        var started = Stopwatch.GetTimestamp();
        try
        {
            frame = _previewDocument.GetFrame(_previewFrameIndex);
        }
        catch
        {
            _previewAnimTimer?.Stop();
            return;
        }

        previewImage.Source = frame;

        var decodeTime = Stopwatch.GetElapsedTime(started);
        var interval = GetPreviewDelay(_previewDocument, _previewFrameIndex) - decodeTime;
        if (_previewAnimTimer is not null)
        {
            _previewAnimTimer.Interval = interval < TimeSpan.FromMilliseconds(1)
                ? TimeSpan.FromMilliseconds(1)
                : interval;
        }
    }

    private static TimeSpan GetPreviewDelay(ImageDocument document, int index)
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

    private async Task RefreshPreviewForAreaAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentPreviewPath))
        {
            return;
        }
        if (_previewDocument is not null)
        {
            return;
        }
        var path = _currentPreviewPath;

        double dpiScale = 1.0;
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is not null)
            {
                dpiScale = source.CompositionTarget.TransformToDevice.M11;
            }
        }
        catch
        {
        }

        var target = ComputePreviewDecodeWidth(
            previewHost.ActualWidth,
            previewHost.ActualHeight,
            dpiScale);

        if (target <= _currentPreviewDecodeWidth)
        {
            // smaller: no redecode, Stretch=Uniform handles display
            return;
        }

        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        var token = cts.Token;

        await LoadPreviewAsync(path, target, cts, token);
    }

    private void UpdateInfoPanel()
    {
        if (!IsInitialized || thumbList is null || infoText is null)
        {
            return;
        }
        var selected = thumbList.SelectedPaths;
        if (selected.Count == 0)
        {
            infoText.Text = $"{_currentFolder}\n총 {_itemCount}개 이미지";
            return;
        }

        if (selected.Count == 1)
        {
            var path = selected[0];
            _ = Task.Run(async () =>
            {
                string text;
                try
                {
                    text = await BuildSingleInfoAsync(path);
                }
                catch
                {
                    text = $"{Path.GetFileName(path)}\n미리보기 불가";
                }
                await Dispatcher.InvokeAsync(() =>
                {
                    if (thumbList.SelectedPaths.Count == 1 && thumbList.SelectedPaths[0] == path)
                    {
                        infoText.Text = text;
                    }
                });
            });
            return;
        }

        var snapshot = selected.ToArray();
        _ = Task.Run(() =>
        {
            long total = 0;
            foreach (var p in snapshot)
            {
                try
                {
                    total += new FileInfo(p).Length;
                }
                catch
                {
                }
            }
            var text = $"{snapshot.Length}개 선택 · 합계 크기 {FormatSize(total)}";
            Dispatcher.InvokeAsync(() => infoText.Text = text);
        });
    }

    private static Task<string> BuildSingleInfoAsync(string path)
    {
        return Task.Run(() =>
        {
            var name = Path.GetFileName(path);
            var folder = Path.GetDirectoryName(path) ?? string.Empty;
            var resolution = "-";
            try
            {
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                if (decoder.Frames.Count > 0)
                {
                    var frame = decoder.Frames[0];
                    resolution = $"{frame.PixelWidth}×{frame.PixelHeight}";
                }
            }
            catch
            {
            }

            var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            if (string.IsNullOrEmpty(ext))
            {
                ext = "-";
            }

            string sizeStr;
            try
            {
                sizeStr = FormatSize(new FileInfo(path).Length);
            }
            catch
            {
                sizeStr = "-";
            }

            string modified = "-";
            try
            {
                modified = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
            }

            return $"{name}\n폴더: {folder}\n해상도: {resolution}\n형식: {ext}\n크기: {sizeStr}\n수정: {modified}";
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        var kb = bytes / 1024.0;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }
        var mb = kb / 1024.0;
        return $"{mb:0.##} MB";
    }

    private static int SortKeyToIndex(ListSortKey key) => key switch
    {
        ListSortKey.Name => 0,
        ListSortKey.Modified => 1,
        ListSortKey.Size => 2,
        ListSortKey.Extension => 3,
        _ => 0
    };

    private static ListSortKey IndexToSortKey(int index) => index switch
    {
        0 => ListSortKey.Name,
        1 => ListSortKey.Modified,
        2 => ListSortKey.Size,
        3 => ListSortKey.Extension,
        _ => ListSortKey.Name
    };

    private void UpdateActionButtons()
    {
        if (!IsInitialized || thumbList is null)
        {
            return;
        }
        var has = thumbList.SelectedPaths.Count > 0;
        if (deleteButton is not null)
        {
            deleteButton.IsEnabled = has;
        }
        if (copyButton is not null)
        {
            copyButton.IsEnabled = has;
        }
        if (moveButton is not null)
        {
            moveButton.IsEnabled = has;
        }
    }

    private void UpdateNavigationButtons()
    {
        if (backButton != null)
        {
            backButton.IsEnabled = _backStack.Count > 0;
        }
        if (forwardButton != null)
        {
            forwardButton.IsEnabled = _forwardStack.Count > 0;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backStack.Count == 0) return;
        var prev = _backStack[_backStack.Count - 1];
        _backStack.RemoveAt(_backStack.Count - 1);
        if (!string.IsNullOrEmpty(_currentFolder))
        {
            _forwardStack.Add(_currentFolder);
        }
        _ = LoadFolder(prev, true);
        UpdateNavigationButtons();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_forwardStack.Count == 0) return;
        var next = _forwardStack[_forwardStack.Count - 1];
        _forwardStack.RemoveAt(_forwardStack.Count - 1);
        if (!string.IsNullOrEmpty(_currentFolder))
        {
            _backStack.Add(_currentFolder);
        }
        _ = LoadFolder(next, true);
        UpdateNavigationButtons();
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFolder)) return;
        try
        {
            var parent = Directory.GetParent(_currentFolder);
            if (parent != null && !string.IsNullOrEmpty(parent.FullName))
            {
                _ = LoadFolder(parent.FullName);
            }
        }
        catch
        {
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentFolder))
        {
            _ = LoadFolder(_currentFolder);
        }
    }

    private async void SlideshowButton_Click(object sender, RoutedEventArgs e)
    {
        var main = Application.Current.MainWindow as MainWindow;
        if (main is null) return;

        if (main.IsSlideshowRunning)
        {
            main.ToggleSlideshow();
            ShowNotice("슬라이드쇼 중지");
            return;
        }

        if (thumbList.SelectedPaths.Count > 0)
        {
            var selectedPath = thumbList.SelectedPaths[thumbList.SelectedPaths.Count - 1];
            OpenInViewerPath = selectedPath;
            await main.OpenPathAndStartSlideshowAsync(selectedPath);
            ShowNotice("슬라이드쇼 시작");
            main.Show();
            main.Activate();
            Close();
        }
        else
        {
            main.ToggleSlideshow();
            main.Show();
            main.Activate();
            ShowNotice(main.IsSlideshowRunning ? "슬라이드쇼 시작" : "슬라이드쇼 중지");
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.OpenSettings(this);
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (addressBox == null) return;
        var path = addressBox.Text?.Trim();
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (Directory.Exists(path))
            {
                _ = LoadFolder(path);
            }
            else
            {
                MessageBox.Show(this, "유효한 디렉터리가 아닙니다.", "주소줄", MessageBoxButton.OK, MessageBoxImage.Warning);
                if (!string.IsNullOrEmpty(_currentFolder))
                {
                    addressBox.Text = _currentFolder;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "주소줄", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (!string.IsNullOrEmpty(_currentFolder))
            {
                addressBox.Text = _currentFolder;
            }
        }
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _applyingUI || sizeSlider is null || sizeValueText is null || thumbList is null)
        {
            return;
        }

        var size = (int)Math.Round(sizeSlider.Value);
        size = Math.Clamp(size, 64, 320);
        _currentPrefs.ThumbSize = size;
        thumbList.TileSize = size;
        sizeValueText.Text = size.ToString();
        _prefsSaveTimer?.Stop();
        _prefsSaveTimer?.Start();
    }

    private void SizeSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SavePrefsNow();
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _applyingUI || sortCombo is null || thumbList is null)
        {
            return;
        }

        _currentPrefs.SortKey = IndexToSortKey(sortCombo.SelectedIndex);
        ReapplySort();
        SavePrefsNow();
    }

    private void SortDescToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _applyingUI || sortDescToggle is null || thumbList is null)
        {
            return;
        }

        _currentPrefs.SortDescending = sortDescToggle.IsChecked == true;
        ReapplySort();
        SavePrefsNow();
    }

    private void ReapplySort()
    {
        if (_currentEntries.Count == 0)
        {
            return;
        }

        var sorted = ListSorter.Sort(_currentEntries, _currentPrefs.SortKey, _currentPrefs.SortDescending);
        _currentEntries.Clear();
        _currentEntries.AddRange(sorted);
        var items = sorted.Select(e => new ThumbItem(e.Path, e.Name)).ToList();
        thumbList.SetItems(items);
        _ = LoadThumbnailResolutionsAsync(items, _folderCts?.Token ?? CancellationToken.None);
        UpdateActionButtons();
    }

    private void SavePrefsNow()
    {
        if (string.IsNullOrWhiteSpace(_currentFolder))
        {
            return;
        }

        ListViewPrefsResolver.Set(_settings, _currentFolder, _currentPrefs);
        _saveSettings();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedAsync();
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        await CopySelectedAsync();
    }

    private async void MoveButton_Click(object sender, RoutedEventArgs e)
    {
        await MoveSelectedAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        var files = thumbList.SelectedPaths.ToList();
        if (files.Count == 0)
        {
            return;
        }

        var useRecycleBin = _settings.UseRecycleBin;
        var ask = _settings.ConfirmDelete || !useRecycleBin;
        if (ask)
        {
            var action = useRecycleBin ? "휴지통으로 이동" : "영구 삭제";
            var message = $"선택한 {files.Count}개 파일을 삭제할까요?({(useRecycleBin ? "휴지통" : "영구")})";
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

        int firstIndex = -1;
        if (_currentEntries.Count > 0)
        {
            firstIndex = _currentEntries.FindIndex(e =>
                string.Equals(e.Path, files[0], StringComparison.OrdinalIgnoreCase));
            if (firstIndex < 0)
            {
                firstIndex = 0;
            }
        }

        progressText.Text = "삭제 중…";
        progressText.Visibility = Visibility.Visible;
        FileOpResult result;
        try
        {
            result = await Task.Run(() => FileOperationService.Delete(files, useRecycleBin));
        }
        finally
        {
            progressText.Visibility = Visibility.Collapsed;
        }

        if (result.Failures.Count > 0)
        {
            var msg = $"실패 {result.Failures.Count}건\n" +
                      string.Join("\n", result.Failures.Take(3).Select(f => $"{Path.GetFileName(f.Path)}: {f.Error}"));
            MessageBox.Show(this, msg, "삭제", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await LoadFolder(_currentFolder);

        if (_currentEntries.Count > 0)
        {
            var idx = firstIndex >= 0
                ? Math.Clamp(firstIndex, 0, _currentEntries.Count - 1)
                : 0;
            thumbList.SelectPath(_currentEntries[idx].Path);
        }
    }

    private async Task CopySelectedAsync()
    {
        var files = thumbList.SelectedPaths.ToList();
        if (files.Count == 0)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "복사 대상 폴더 선택" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var dest = dialog.FolderName;
        var destFull = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentFull = Path.GetFullPath(_currentFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var same = string.Equals(destFull, currentFull, StringComparison.OrdinalIgnoreCase);

        progressText.Visibility = Visibility.Visible;
        progressText.Text = $"0/{files.Count} 처리 중…";

        var progress = new Progress<int>(p =>
        {
            var done = files.Count == 0 ? 0 : (int)Math.Round(p * files.Count / 100.0);
            if (done < 1 && p > 0)
            {
                done = 1;
            }
            if (done > files.Count)
            {
                done = files.Count;
            }
            progressText.Text = $"{done}/{files.Count} 처리 중…";
        });

        FileOpResult result;
        try
        {
            result = await Task.Run(() => FileOperationService.Copy(files, dest, progress));
        }
        finally
        {
            progressText.Visibility = Visibility.Collapsed;
        }

        if (result.Failures.Count > 0)
        {
            var msg = $"실패 {result.Failures.Count}건\n" +
                      string.Join("\n", result.Failures.Take(3).Select(f => $"{Path.GetFileName(f.Path)}: {f.Error}"));
            MessageBox.Show(this, msg, "복사", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (same)
        {
            await LoadFolder(_currentFolder);
        }
    }

    private async Task MoveSelectedAsync()
    {
        var files = thumbList.SelectedPaths.ToList();
        if (files.Count == 0)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "이동 대상 폴더 선택" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var dest = dialog.FolderName;

        progressText.Visibility = Visibility.Visible;
        progressText.Text = $"0/{files.Count} 처리 중…";

        var progress = new Progress<int>(p =>
        {
            var done = files.Count == 0 ? 0 : (int)Math.Round(p * files.Count / 100.0);
            if (done < 1 && p > 0)
            {
                done = 1;
            }
            if (done > files.Count)
            {
                done = files.Count;
            }
            progressText.Text = $"{done}/{files.Count} 처리 중…";
        });

        FileOpResult result;
        try
        {
            result = await Task.Run(() => FileOperationService.Move(files, dest, progress));
        }
        finally
        {
            progressText.Visibility = Visibility.Collapsed;
        }

        if (result.Failures.Count > 0)
        {
            var msg = $"실패 {result.Failures.Count}건\n" +
                      string.Join("\n", result.Failures.Take(3).Select(f => $"{Path.GetFileName(f.Path)}: {f.Error}"));
            MessageBox.Show(this, msg, "이동", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await LoadFolder(_currentFolder);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var inTextInput = Keyboard.FocusedElement is TextBox
            || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase
            || e.OriginalSource is ComboBox;

        // 입력 필드에서는 문자·편집 키(Delete, 방향키, Enter 등)를 필드에 남기고,
        // F키와 Alt 조합만 keymap 으로 처리한다. Esc 는 기존대로 창을 닫는다.
        if (inTextInput)
        {
            if (e.Key == Key.Escape)
            {
                OpenInViewerPath = null;
                Close();
                e.Handled = true;
                return;
            }

            if (ShortcutService.IsTextEditingKey(e))
            {
                return;
            }
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && shift && e.Key == Key.C)
        {
            _ = CopySelectedAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.M)
        {
            _ = MoveSelectedAsync();
            e.Handled = true;
            return;
        }

        // 목록에 포커스가 있으면 방향·Home/End·Enter 등 탐색 키는 목록이 처리한다.
        // (이전에는 목록 포커스 중 모든 단축키가 여기서 막혀 F키·Alt 조합이 동작하지 않았다.)
        if (thumbList is not null && thumbList.IsKeyboardFocusWithin && ShortcutService.IsListNavigationKey(e))
        {
            return;
        }

        var action = ShortcutService.FindAction(_shortcuts, e);
        if (action is null)
        {
            return;
        }

        e.Handled = true;
        if (ShortcutService.IsUnavailableIn(action, ShortcutService.Screen.List))
        {
            ShowNotice(ShortcutService.GetUnavailableMessage(action));
            return;
        }

        ExecuteListViewShortcut(action);
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 마우스 뒤로/앞으로 버튼 = 폴더 히스토리 back/forward (키맵 Back/Forward 와 동일 동작).
        if (e.ChangedButton == MouseButton.XButton1)
        {
            BackButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            ForwardButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    /// <summary>닫기 버튼 없는 자동 소멸 토스트 (MainWindow 의 NoticeBorder 와 동일 방식).</summary>
    private void ShowNotice(string message)
    {
        NoticeText.Text = message;
        NoticeBorder.BeginAnimation(OpacityProperty, null);
        NoticeBorder.Opacity = 1;
        NoticeBorder.Visibility = Visibility.Visible;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void ExecuteListViewShortcut(string id)
    {
        switch (id)
        {
            case "Open":
                {
                    var dialog = new OpenFileDialog
                    {
                        Title = "이미지 또는 압축파일 열기",
                        Filter = MainWindow.OpenFileFilter
                    };

                    if (dialog.ShowDialog(this) == true)
                    {
                        OpenInViewerPath = dialog.FileName;
                        Close();
                    }
                    break;
                }
            case "CloseWindow":
                OpenInViewerPath = null;
                Close();
                break;
            case "ListView":
                // already in list view
                break;
            case "Delete":
                _ = DeleteSelectedAsync();
                break;
            case "Copy":
                _ = CopySelectedAsync();
                break;
            case "Rename":
                RenameSelected();
                break;
            case "Settings":
                {
                    var dialog = new SettingsWindow(
                        _settings.SlideshowIntervalSeconds,
                        _settings.SlideshowRepeat,
                        _settings.SlideshowRandom,
                        _settings.ThumbnailsVisible,
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
                        _settings.Shortcuts,
                        _settings.Theme,
                        _settings.AutoCheckUpdates,
                        _settings.SkippedVersion,
                        _settings.ListViewScope,
                        savedWidth: _settings.SettingsWindowWidth,
                        savedHeight: _settings.SettingsWindowHeight)
                    {
                        Owner = this
                    };

                    var accepted = dialog.ShowDialog() == true;
                    dialog.CopySizeTo(_settings);
                    try { _saveSettings(); } catch { }

                    if (accepted)
                    {
                        _settings.SlideshowIntervalSeconds = Math.Clamp(dialog.IntervalSeconds, 2, 60);
                        _settings.SlideshowRepeat = dialog.SlideshowRepeat;
                        _settings.SlideshowRandom = dialog.SlideshowRandom;
                        _settings.ThumbnailsVisible = dialog.ShowThumbnails;
                        _settings.DefaultZoomPercent = dialog.DefaultZoomPercent;
                        _settings.AutoFitWindowToImage = dialog.AutoFitWindowToImage;
                        _settings.DoNotUpscaleSmallImages = dialog.DoNotUpscaleSmallImages;
                        _settings.ConfirmDelete = dialog.ConfirmDelete;
                        _settings.UseRecycleBin = dialog.UseRecycleBin;
                        _settings.EditorPath = dialog.EditorPath;
                        _settings.SingleWindowMode = dialog.SingleWindowMode;
                        _settings.ListViewScope = dialog.ListViewScope;
                        _settings.WrapNavigation = dialog.WrapNavigation;
                        _settings.ShowNavigationNotice = dialog.ShowNavigationNotice;
                        _settings.Theme = dialog.Theme;
                        _settings.AutoCheckUpdates = dialog.AutoCheckUpdates;
                        if (dialog.ClearSkippedVersion)
                        {
                            _settings.SkippedVersion = string.Empty;
                        }
                        _settings.AssociatedExtensions = dialog.SelectedExtensions.ToList();
                        _settings.AssociateFileTypes = _settings.AssociatedExtensions.Count > 0;
                        _settings.Shortcuts = ShortcutService.ToCustomMap(dialog.SelectedShortcuts);
                        var updated = ShortcutService.GetEffective(_settings.Shortcuts);
                        _shortcuts.Clear();
                        foreach (var kv in updated)
                        {
                            _shortcuts[kv.Key] = kv.Value;
                        }
                        try { _saveSettings(); } catch { }
                    }
                    break;
                }
            case "Fullscreen":
                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                }
                else
                {
                    WindowState = WindowState.Maximized;
                }
                break;
            case "Previous":
                MoveFocusInList(-1);
                break;
            case "Next":
                MoveFocusInList(1);
                break;
            case "First":
                MoveFocusInList(int.MinValue);
                break;
            case "Last":
                MoveFocusInList(int.MaxValue);
                break;
            case "ZoomIn":
                if (sizeSlider != null)
                {
                    var v = sizeSlider.Value + 16;
                    if (v > 320) v = 320;
                    sizeSlider.Value = v;
                }
                break;
            case "ZoomOut":
                if (sizeSlider != null)
                {
                    var v = sizeSlider.Value - 16;
                    if (v < 64) v = 64;
                    sizeSlider.Value = v;
                }
                break;
            case "Fit":
                if (sizeSlider != null) sizeSlider.Value = 160;
                break;
            case "ActualSize":
                if (sizeSlider != null) sizeSlider.Value = 320;
                break;
            case "Info":
            case "Tab":
                ToggleInfoPanel();
                break;
            case "Slideshow":
                SlideshowButton_Click(this, new RoutedEventArgs());
                break;
            case "CloseImage":
            case "Thumbnails":
            case "Edit":
            case "WindowFit":
            default:
                break;
            case "Bookmarks":
                {
                    var selected = thumbList.SelectedPaths.Count > 0
                        ? thumbList.SelectedPaths[thumbList.SelectedPaths.Count - 1]
                        : null;
                    var dialog = new BookmarkWindow(_settings.Bookmarks, selected)
                    {
                        Owner = this
                    };
                    dialog.ShowDialog();
                    _settings.Bookmarks = dialog.Bookmarks.ToList();
                    if (!string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    {
                        var path = dialog.SelectedPath!;
                        if (Directory.Exists(path))
                        {
                            _ = LoadFolder(path);
                        }
                        else if (File.Exists(path))
                        {
                            var folder = Path.GetDirectoryName(path);
                            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                            {
                                _ = LoadFolder(folder);
                                Dispatcher.BeginInvoke(new Action(() => thumbList.SelectPath(path)), DispatcherPriority.Loaded);
                            }
                        }
                    }
                    break;
                }
            case "SaveAs":
                {
                    var files = thumbList.SelectedPaths.ToList();
                    if (files.Count != 1)
                    {
                        break;
                    }
                    var src = files[0];
                    if (Directory.Exists(src))
                    {
                        break;
                    }
                    var ext = Path.GetExtension(src);
                    var filter = string.IsNullOrEmpty(ext)
                        ? "모든 파일 (*.*)|*.*"
                        : $"{ext.TrimStart('.').ToUpperInvariant()} 이미지 (*{ext})|*{ext}|모든 파일 (*.*)|*.*";
                    var sfd = new SaveFileDialog
                    {
                        Title = "다른 이름으로 저장",
                        FileName = Path.GetFileName(src),
                        AddExtension = true,
                        Filter = filter,
                        DefaultExt = ext.TrimStart('.'),
                        FilterIndex = 1
                    };
                    if (sfd.ShowDialog(this) != true)
                    {
                        break;
                    }
                    static bool ExtEqual(string a, string b)
                    {
                        a = (a ?? string.Empty).TrimStart('.').ToLowerInvariant();
                        b = (b ?? string.Empty).TrimStart('.').ToLowerInvariant();
                        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                        {
                            return true;
                        }
                        if (a == b)
                        {
                            return true;
                        }
                        if ((a == "jpeg" && b == "jpg") || (a == "jpg" && b == "jpeg"))
                        {
                            return true;
                        }
                        if ((a == "tiff" && b == "tif") || (a == "tif" && b == "tiff"))
                        {
                            return true;
                        }
                        return false;
                    }
                    if (!ExtEqual(ext, Path.GetExtension(sfd.FileName)))
                    {
                        MessageBox.Show(this, "다른 이미지 형식으로 저장하는 기능은 지원하지 않습니다.\n같은 확장자로 저장해 주세요.", "다른 이름으로 저장", MessageBoxButton.OK, MessageBoxImage.Information);
                        break;
                    }
                    try
                    {
                        File.Copy(src, sfd.FileName, true);
                        var destFolder = Path.GetDirectoryName(sfd.FileName);
                        if (!string.IsNullOrWhiteSpace(destFolder) &&
                            string.Equals(Path.GetFullPath(destFolder), Path.GetFullPath(_currentFolder), StringComparison.OrdinalIgnoreCase))
                        {
                            _ = LoadFolder(_currentFolder);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"파일을 저장할 수 없습니다.\n{ex.Message}", "다른 이름으로 저장", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    break;
                }
            case "ParentFolder":
                UpButton_Click(this, new RoutedEventArgs());
                break;
            case "Back":
                BackButton_Click(this, new RoutedEventArgs());
                break;
            case "Forward":
                ForwardButton_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private void MoveFocusInList(int direction)
    {
        if (thumbList == null) return;
        var paths = _currentEntries.Select(e => e.Path).ToList();
        if (paths.Count == 0) return;

        string? current = thumbList.FocusedPath;
        if (string.IsNullOrEmpty(current) && thumbList.SelectedPaths.Count > 0)
        {
            current = thumbList.SelectedPaths[thumbList.SelectedPaths.Count - 1];
        }
        if (string.IsNullOrEmpty(current))
        {
            current = paths[0];
        }

        int idx = paths.FindIndex(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) idx = 0;

        if (direction == int.MinValue)
        {
            idx = 0;
        }
        else if (direction == int.MaxValue)
        {
            idx = paths.Count - 1;
        }
        else
        {
            idx = (idx + direction + paths.Count) % paths.Count;
        }

        if (thumbList.SelectedPaths.Count <= 1)
        {
            thumbList.SelectPath(paths[idx]);
        }
        else
        {
            // try to move focus/selection as much as possible with available API
            try
            {
                thumbList.SelectPath(paths[idx]);
            }
            catch { }
        }
    }

    private void RenameSelected()
    {
        var files = thumbList.SelectedPaths.ToList();
        if (files.Count != 1) return;
        var oldPath = files[0];
        if (!File.Exists(oldPath)) return;
        var oldName = Path.GetFileName(oldPath);
        try
        {
            var dialog = new RenameWindow(oldName) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var newName = dialog.NewName.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;
            if (string.Equals(newName, oldName, StringComparison.Ordinal)) return;
            var dir = Path.GetDirectoryName(oldPath) ?? _currentFolder;
            var newPath = Path.Combine(dir, newName);
            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(newPath))
            {
                MessageBox.Show(this, "같은 이름의 파일이 이미 있습니다.", "이름 바꾸기", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            File.Move(oldPath, newPath);
            _ = LoadFolder(_currentFolder);
        }
        catch
        {
            // ignore
        }
    }

    private void ToggleInfoPanel()
    {
        try
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(infoText);
            if (parent != null)
            {
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            if (parent is Border border)
            {
                border.Visibility = border.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                return;
            }
            // fallback
            if (parent is FrameworkElement fe && fe.Parent is Border border2)
            {
                border2.Visibility = border2.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                return;
            }
        }
        catch { }
    }
}