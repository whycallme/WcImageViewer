using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WcImageViewer.Controls;

public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(64.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(64.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private double _offset;
    private Size _extent = new Size(0, 0);
    private Size _viewport = new Size(0, 0);
    private int _columns = 1;
    private int _itemCount;

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;

    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;

    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight);
    public void LineDown() => SetVerticalOffset(VerticalOffset + ItemHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - ItemHeight * 3);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + ItemHeight * 3);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }

    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        var clamped = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        var changed = Math.Abs(clamped - _offset) > 0.001;
        _offset = clamped;
        if (changed)
        {
            InvalidateMeasure();
        }
        ScrollOwner?.InvalidateScrollInfo();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is not FrameworkElement fe)
        {
            return Rect.Empty;
        }

        var index = ((System.Windows.Controls.ItemContainerGenerator)ItemContainerGenerator).IndexFromContainer(fe);
        if (index < 0)
        {
            return Rect.Empty;
        }

        BringIndexIntoView(index);
        return rectangle;
    }

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= _itemCount || _columns <= 0)
        {
            return;
        }

        var row = index / _columns;
        var itemTop = row * ItemHeight;
        var itemBottom = itemTop + ItemHeight;

        if (itemTop < _offset)
        {
            SetVerticalOffset(itemTop);
        }
        else if (itemBottom > _offset + _viewport.Height)
        {
            SetVerticalOffset(itemBottom - _viewport.Height);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        _itemCount = itemsControl?.Items.Count ?? 0;

        var viewportWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? 0
            : availableSize.Width;
        if (viewportWidth <= 0)
        {
            viewportWidth = _viewport.Width > 0 ? _viewport.Width : ItemWidth;
        }

        _columns = Math.Max(1, (int)Math.Floor(viewportWidth / Math.Max(1, ItemWidth)));

        var totalRows = _itemCount == 0 ? 0 : (_itemCount + _columns - 1) / _columns;
        _extent = new Size(viewportWidth, totalRows * ItemHeight);

        var viewportHeight = double.IsInfinity(availableSize.Height) || availableSize.Height <= 0
            ? (_extent.Height > 0 ? _extent.Height : 0)
            : availableSize.Height;
        _viewport = new Size(viewportWidth, viewportHeight);

        _offset = Math.Clamp(_offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        ScrollOwner?.InvalidateScrollInfo();

        if (_itemCount == 0)
        {
            CleanContainers();
            return _viewport;
        }

        var firstVisibleRow = (int)Math.Floor(_offset / Math.Max(1, ItemHeight));
        var visibleRows = (int)Math.Ceiling(viewportHeight / Math.Max(1, ItemHeight));
        var lastVisibleRow = firstVisibleRow + Math.Max(0, visibleRows - 1);

        var startRow = Math.Max(0, firstVisibleRow - 1);
        var endRow = Math.Min(totalRows - 1, lastVisibleRow + 1);

        var startIndex = startRow * _columns;
        var endIndex = Math.Min(_itemCount - 1, (endRow + 1) * _columns - 1);

        RemoveOutOfRange(startIndex, endIndex);
        Realize(startIndex, endIndex);

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(ItemWidth, ItemHeight));
        }

        return _viewport;
    }

    private void RemoveOutOfRange(int startIndex, int endIndex)
    {
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var child = InternalChildren[i];
            var itemIndex = ((System.Windows.Controls.ItemContainerGenerator)ItemContainerGenerator).IndexFromContainer(child);
            if (itemIndex < 0)
            {
                continue;
            }

            if (itemIndex < startIndex || itemIndex > endIndex)
            {
                var position = ItemContainerGenerator.GeneratorPositionFromIndex(itemIndex);
                ItemContainerGenerator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    private void Realize(int startIndex, int endIndex)
    {
        if (startIndex > endIndex)
        {
            return;
        }

        var startPosition = ItemContainerGenerator.GeneratorPositionFromIndex(startIndex);
        var childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using (ItemContainerGenerator.StartAt(startPosition, GeneratorDirection.Forward, true))
        {
            for (var i = startIndex; i <= endIndex; i++)
            {
                var isNew = false;
                var container = ItemContainerGenerator.GenerateNext(out isNew) as UIElement;
                if (container is null)
                {
                    break;
                }

                if (isNew)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(container);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, container);
                    }

                    ItemContainerGenerator.PrepareItemContainer(container);
                }

                childIndex++;
            }
        }
    }

    private void CleanContainers()
    {
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var child = InternalChildren[i];
            var itemIndex = ((System.Windows.Controls.ItemContainerGenerator)ItemContainerGenerator).IndexFromContainer(child);
            if (itemIndex < 0)
            {
                continue;
            }

            var position = ItemContainerGenerator.GeneratorPositionFromIndex(itemIndex);
            ItemContainerGenerator.Remove(position, 1);
            RemoveInternalChildRange(i, 1);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var index = ((System.Windows.Controls.ItemContainerGenerator)ItemContainerGenerator).IndexFromContainer(child);
            if (index < 0)
            {
                continue;
            }

            var row = index / _columns;
            var col = index % _columns;
            var x = col * ItemWidth;
            var y = row * ItemHeight - _offset;
            child.Arrange(new Rect(x, y, ItemWidth, ItemHeight));
        }

        if (finalSize.Width > 0)
        {
            _viewport.Width = finalSize.Width;
        }

        if (finalSize.Height > 0)
        {
            _viewport.Height = finalSize.Height;
        }

        ScrollOwner?.InvalidateScrollInfo();

        return finalSize.Width > 0 && finalSize.Height > 0 ? finalSize : _viewport;
    }
}
