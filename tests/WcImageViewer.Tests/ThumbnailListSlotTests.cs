using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WcImageViewer.Controls;

namespace WcImageViewer.Tests;

public class ThumbnailListSlotTests
{
    [Fact]
    public void ComputeSlotSize_CoversChromeCaptionMarginsAndTwoCaptionLines()
    {
        var size = ThumbnailListControl.ComputeSlotSize(160, 12, 17);

        Assert.True(size.Width >= 160 + 8 + 12, $"width {size.Width} < {160 + 8 + 12}");
        Assert.True(size.Height >= 160 + 32 + 2 + 2 * 17 + 12, $"height {size.Height} < {160 + 32 + 2 + 2 * 17 + 12}");
    }

    [Fact]
    public void ComputeSlotSize_UsesAtLeastFontSizeTimes1Point4PerLine()
    {
        var fromSmallLineHeight = ThumbnailListControl.ComputeSlotSize(160, 12, 0);

        Assert.True(fromSmallLineHeight.Height >= 160 + 32 + 2 + 2 * Math.Ceiling(12 * 1.4) + 12);
    }

    [Fact]
    public void ComputeSlotSize_GrowsWithTileAndFontSize()
    {
        var small = ThumbnailListControl.ComputeSlotSize(96, 11, 15);
        var bigTile = ThumbnailListControl.ComputeSlotSize(256, 11, 15);
        var bigFont = ThumbnailListControl.ComputeSlotSize(96, 14, 15);

        Assert.True(bigTile.Width > small.Width && bigTile.Height > small.Height);
        Assert.True(bigFont.Height > small.Height);
    }

    private const string LongName = "매우_긴_파일명_테스트_이미지_abcdefghijklmnopqrstuvwxyz_0123456789_extra_long.png";
    private const string LongPath = @"C:\images\매우_긴_파일명_테스트_이미지_abcdefghijklmnopqrstuvwxyz_0123456789_extra_long.png";

    public static IEnumerable<object[]> TileAndFontCombinations()
    {
        foreach (var tile in new[] { 96, 160, 256 })
        {
            foreach (var font in new[] { 11.0, 12.0, 14.0 })
            {
                yield return new object[] { tile, font };
            }
        }
    }

    [Theory]
    [MemberData(nameof(TileAndFontCombinations))]
    public void ListBoxItem_DesiredSize_FitsInsideSlot(int tileSize, double fontSize)
    {
        var result = StaRunner.Run(() => MeasureItem(tileSize, fontSize));

        Assert.True(
            result.Item.Width <= result.Slot.Width + 0.5,
            $"tile={tileSize} font={fontSize}: item width {result.Item.Width:F1} > slot width {result.Slot.Width:F1}");
        Assert.True(
            result.Item.Height <= result.Slot.Height + 0.5,
            $"tile={tileSize} font={fontSize}: item height {result.Item.Height:F1} > slot height {result.Slot.Height:F1}");
    }

    [Theory]
    [MemberData(nameof(TileAndFontCombinations))]
    public void FileSizeText_FitsInsideCell(int tileSize, double fontSize)
    {
        var result = StaRunner.Run(() => MeasureItem(tileSize, fontSize));

        Assert.True(
            result.FileSizeWidth <= tileSize + 8 + 0.5,
            $"tile={tileSize} font={fontSize}: file size width {result.FileSizeWidth:F1} > cell width {tileSize + 8}");
    }

    private static (Size Item, Size Slot, double FileSizeWidth) MeasureItem(int tileSize, double fontSize)
    {
        var control = new ThumbnailListControl { TileSize = tileSize, ItemFontSize = fontSize };
        control.SetItems(new List<ThumbItem>
        {
            new ThumbItem(LongPath, LongName) { Resolution = "1500x1013", FileSize = "572.9 KB" },
        });

        var window = new Window { Content = control, Width = 1200, Height = 900, ShowInTaskbar = false };
        window.Show();
        try
        {
            window.UpdateLayout();
            var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);

            var item = FindDescendant<ListBoxItem>(control)
                ?? throw new InvalidOperationException("ListBoxItem was not generated.");
            item.Measure(infinite);

            var sizeText = FindDescendantText(item, "572.9 KB")
                ?? throw new InvalidOperationException("File size TextBlock not found.");
            sizeText.Measure(infinite);

            var slot = ThumbnailListControl.ComputeSlotSize(
                tileSize,
                fontSize,
                ThumbnailListControl.MeasureLineHeight(control.FontFamily, fontSize));

            return (item.DesiredSize, slot, sizeText.DesiredSize.Width);
        }
        finally
        {
            window.Close();
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static TextBlock? FindDescendantText(DependencyObject root, string text)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb && tb.Text == text)
            {
                return tb;
            }

            var nested = FindDescendantText(child, text);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
