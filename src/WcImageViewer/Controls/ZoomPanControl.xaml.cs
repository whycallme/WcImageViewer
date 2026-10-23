using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WcImageViewer.Models;

namespace WcImageViewer.Controls;

public partial class ZoomPanControl : UserControl
{
    private const double MinZoom = 0.01;
    private const double MaxZoom = 32.0;

    private BitmapSource? _source;
    private IImageTileSource? _tiles;
    private double _imageWidth;
    private double _imageHeight;
    private double _tileThreshold = double.MaxValue;
    private int _tileLevel;
    private int _tileGeneration;
    private readonly Dictionary<(int Column, int Row), Image> _tileElements = new();
    private readonly HashSet<(int Column, int Row)> _visibleTiles = new();
    private double _zoom = 1;
    private double _offsetX;
    private double _offsetY;
    private bool _isFit = true;
    private bool _isPanning;
    private Point _lastPanPoint;

    public ZoomPanControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_isFit)
            {
                FitToWindow();
            }
        };
        SizeChanged += (_, _) =>
        {
            if (_isFit)
            {
                FitToWindow();
            }
            else
            {
                ClampOffsets();
                ApplyTransform();
            }
        };
    }

    public event EventHandler? ZoomChanged;

    public event EventHandler<int>? NavigateRequested;

    public event EventHandler? WindowDragRequested;

    public double Zoom => _zoom;

    /// <summary>When false, fit-to-window never enlarges an image past 100%.</summary>
    public bool AllowUpscaleOnFit { get; set; } = true;

    public bool HasImage => _source is not null;

    public void SetImage(BitmapSource source, IImageTileSource? tiles = null)
    {
        _source = source;
        _tiles = tiles;
        _tileGeneration++;
        ClearTileElements();
        _tileLevel = 0;

        if (tiles is not null)
        {
            var previewScale = Math.Min(
                (double)source.PixelWidth / tiles.PixelWidth,
                (double)source.PixelHeight / tiles.PixelHeight);
            _tileThreshold = Math.Max(0.25, previewScale * 1.05);
        }
        else
        {
            _tileThreshold = double.MaxValue;
        }

        _imageWidth = tiles?.PixelWidth ?? source.PixelWidth;
        _imageHeight = tiles?.PixelHeight ?? source.PixelHeight;

        ViewerImage.Source = source;
        // Ignore embedded DPI: one image pixel is one DIP here, then zoom on top.
        ViewerImage.Width = _imageWidth;
        ViewerImage.Height = _imageHeight;
        _zoom = 1;
        _isFit = true;
        FitToWindow();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Swap a frame without resetting the current zoom/pan.</summary>
    public void SetFrame(BitmapSource source)
    {
        _source = source;
        _imageWidth = source.PixelWidth;
        _imageHeight = source.PixelHeight;
        ViewerImage.Source = source;

        var sizeChanged =
            Math.Abs(ViewerImage.Width - _imageWidth) > 0.1 ||
            Math.Abs(ViewerImage.Height - _imageHeight) > 0.1;

        ViewerImage.Width = _imageWidth;
        ViewerImage.Height = _imageHeight;

        if (!sizeChanged)
        {
            ApplyTransform();
            return;
        }

        if (_isFit)
        {
            FitToWindow();
        }
        else
        {
            ClampOffsets();
            ApplyTransform();
        }
    }

    public void Clear()
    {
        _source = null;
        _tiles = null;
        _tileGeneration++;
        ClearTileElements();
        _tileThreshold = double.MaxValue;
        _imageWidth = 0;
        _imageHeight = 0;
        ViewerImage.Source = null;
        _zoom = 1;
        _offsetX = 0;
        _offsetY = 0;
        _isFit = true;
        ApplyTransform();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FitToWindow()
    {
        if (_source is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var fit = Math.Min(ActualWidth / _imageWidth, ActualHeight / _imageHeight);
        if (!AllowUpscaleOnFit)
        {
            fit = Math.Min(fit, 1.0);
        }

        _zoom = Math.Clamp(fit, MinZoom, MaxZoom);
        _offsetX = (ActualWidth - _imageWidth * _zoom) / 2;
        _offsetY = (ActualHeight - _imageHeight * _zoom) / 2;
        _isFit = true;
        ApplyTransform();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ActualSize() => SetZoom(1);

    public void SetZoom(double zoom)
    {
        if (_source is null)
        {
            return;
        }

        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _offsetX = (ActualWidth - _imageWidth * _zoom) / 2;
        _offsetY = (ActualHeight - _imageHeight * _zoom) / 2;
        _isFit = false;
        ClampOffsets();
        ApplyTransform();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomIn() => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), 1.25);

    public void ZoomOut() => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), 0.8);

    private void ZoomAt(Point anchor, double factor)
    {
        if (_source is null)
        {
            return;
        }

        var oldZoom = _zoom;
        var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 1e-9)
        {
            return;
        }

        var imageX = (anchor.X - _offsetX) / oldZoom;
        var imageY = (anchor.Y - _offsetY) / oldZoom;

        _zoom = newZoom;
        _offsetX = anchor.X - imageX * newZoom;
        _offsetY = anchor.Y - imageY * newZoom;
        _isFit = false;

        ClampOffsets();
        ApplyTransform();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClampOffsets()
    {
        if (_source is null)
        {
            return;
        }

        var width = _imageWidth * _zoom;
        var height = _imageHeight * _zoom;

        _offsetX = width <= ActualWidth
            ? (ActualWidth - width) / 2
            : Math.Clamp(_offsetX, ActualWidth - width, 0);

        _offsetY = height <= ActualHeight
            ? (ActualHeight - height) / 2
            : Math.Clamp(_offsetY, ActualHeight - height, 0);
    }

    private void ApplyTransform()
    {
        ImageTransform.Matrix = new Matrix(_zoom, 0, 0, _zoom, _offsetX, _offsetY);
        TileTransform.Matrix = ImageTransform.Matrix;

        var scalingMode = _isFit || _zoom < 1
            ? BitmapScalingMode.HighQuality
            : BitmapScalingMode.NearestNeighbor;
        RenderOptions.SetBitmapScalingMode(ViewerImage, scalingMode);
        foreach (var tile in _tileElements.Values)
        {
            RenderOptions.SetBitmapScalingMode(tile, scalingMode);
        }

        UpdateTiles();
    }

    private void UpdateTiles()
    {
        if (_tiles is null || _source is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            if (_tileElements.Count > 0)
            {
                ClearTileElements();
            }

            return;
        }

        if (_zoom < _tileThreshold)
        {
            if (_tileElements.Count > 0)
            {
                ClearTileElements();
            }

            return;
        }

        var level = _tiles.GetLevelForZoom(_zoom);
        if (level != _tileLevel)
        {
            _tileLevel = level;
            ClearTileElements();
        }

        var viewportWidth = ActualWidth / _zoom;
        var viewportHeight = ActualHeight / _zoom;
        var viewportLeft = -_offsetX / _zoom;
        var viewportTop = -_offsetY / _zoom;
        var margin = _tiles.BaseTileSize << -level;

        _visibleTiles.Clear();

        foreach (var (column, row) in _tiles.GetVisibleTiles(
                     viewportLeft - margin,
                     viewportTop - margin,
                     viewportWidth + margin * 2,
                     viewportHeight + margin * 2,
                     level))
        {
            _visibleTiles.Add((column, row));
            if (!_tileElements.ContainsKey((column, row)))
            {
                _ = LoadTileAsync(column, row, level);
            }
        }

        foreach (var key in _tileElements.Keys.Where(key => !_visibleTiles.Contains(key)).ToArray())
        {
            TileCanvas.Children.Remove(_tileElements[key]);
            _tileElements.Remove(key);
        }
    }

    private async Task LoadTileAsync(int column, int row, int level)
    {
        var tiles = _tiles;
        if (tiles is null)
        {
            return;
        }

        var generation = _tileGeneration;

        try
        {
            var tile = await tiles.GetTileAsync(column, row, level, CancellationToken.None);
            if (tile is null ||
                generation != _tileGeneration ||
                level != _tileLevel ||
                !_visibleTiles.Contains((column, row)) ||
                _tileElements.ContainsKey((column, row)))
            {
                return;
            }

            var image = new Image
            {
                Source = tile.Bitmap,
                Width = tile.Width,
                Height = tile.Height,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true
            };

            Canvas.SetLeft(image, tile.Left);
            Canvas.SetTop(image, tile.Top);
            RenderOptions.SetBitmapScalingMode(
                image,
                _isFit || _zoom < 1 ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);

            _tileElements[(column, row)] = image;
            TileCanvas.Children.Add(image);
        }
        catch
        {
            // Keep the preview visible when a tile cannot be decoded.
        }
    }

    private void ClearTileElements()
    {
        TileCanvas.Children.Clear();
        _tileElements.Clear();
        _visibleTiles.Clear();
    }

    private bool CanPan()
    {
        if (_source is null)
        {
            return false;
        }

        return _imageWidth * _zoom > ActualWidth + 0.5 ||
               _imageHeight * _zoom > ActualHeight + 0.5;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_source is null)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var factor = Math.Pow(1.15, e.Delta / 120.0);
            ZoomAt(e.GetPosition(this), factor);
        }
        else
        {
            NavigateRequested?.Invoke(this, e.Delta > 0 ? -1 : 1);
        }

        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_source is null)
        {
            WindowDragRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            if (_isFit)
            {
                ActualSize();
            }
            else
            {
                FitToWindow();
            }

            e.Handled = true;
            return;
        }

        if (!CanPan())
        {
            WindowDragRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _lastPanPoint = e.GetPosition(this);
        CaptureMouse();
        Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var point = e.GetPosition(this);
        _offsetX += point.X - _lastPanPoint.X;
        _offsetY += point.Y - _lastPanPoint.Y;
        _lastPanPoint = point;

        ClampOffsets();
        ApplyTransform();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        e.Handled = true;
    }
}
