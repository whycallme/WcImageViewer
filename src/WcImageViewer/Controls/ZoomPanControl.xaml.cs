using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WcImageViewer.Controls;

public partial class ZoomPanControl : UserControl
{
    private const double MinZoom = 0.01;
    private const double MaxZoom = 32.0;

    private BitmapSource? _source;
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

    public bool HasImage => _source is not null;

    public void SetImage(BitmapSource source)
    {
        _source = source;
        ViewerImage.Source = source;
        // Ignore embedded DPI: one image pixel is one DIP here, then zoom on top.
        ViewerImage.Width = source.PixelWidth;
        ViewerImage.Height = source.PixelHeight;
        _zoom = 1;
        _isFit = true;
        FitToWindow();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Swap a frame without resetting the current zoom/pan.</summary>
    public void SetFrame(BitmapSource source)
    {
        _source = source;
        ViewerImage.Source = source;

        var sizeChanged =
            Math.Abs(ViewerImage.Width - source.PixelWidth) > 0.1 ||
            Math.Abs(ViewerImage.Height - source.PixelHeight) > 0.1;

        ViewerImage.Width = source.PixelWidth;
        ViewerImage.Height = source.PixelHeight;

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

        var fit = Math.Min(ActualWidth / _source.PixelWidth, ActualHeight / _source.PixelHeight);
        _zoom = Math.Clamp(fit, MinZoom, MaxZoom);
        _offsetX = (ActualWidth - _source.PixelWidth * _zoom) / 2;
        _offsetY = (ActualHeight - _source.PixelHeight * _zoom) / 2;
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
        _offsetX = (ActualWidth - _source.PixelWidth * _zoom) / 2;
        _offsetY = (ActualHeight - _source.PixelHeight * _zoom) / 2;
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

        var width = _source.PixelWidth * _zoom;
        var height = _source.PixelHeight * _zoom;

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
        RenderOptions.SetBitmapScalingMode(
            ViewerImage,
            _isFit || _zoom < 1 ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
    }

    private bool CanPan()
    {
        if (_source is null)
        {
            return false;
        }

        return _source.PixelWidth * _zoom > ActualWidth + 0.5 ||
               _source.PixelHeight * _zoom > ActualHeight + 0.5;
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
