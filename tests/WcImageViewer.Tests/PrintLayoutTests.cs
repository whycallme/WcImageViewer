using System.Windows;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class PrintLayoutTests
{
    [Fact]
    public void A4PortraitPaperSize()
    {
        var s = PrintLayoutCalculator.GetPaperSizeDip(PrintPaperSize.A4, PrintOrientation.Portrait);
        Assert.Equal(793.7, s.Width, 1);
        Assert.Equal(1122.5, s.Height, 1);
    }

    [Fact]
    public void A4LandscapePaperSize()
    {
        var s = PrintLayoutCalculator.GetPaperSizeDip(PrintPaperSize.A4, PrintOrientation.Landscape);
        Assert.Equal(1122.5, s.Width, 1);
        Assert.Equal(793.7, s.Height, 1);
    }

    [Fact]
    public void A0PortraitPaperSize()
    {
        var s = PrintLayoutCalculator.GetPaperSizeDip(PrintPaperSize.A0, PrintOrientation.Portrait);
        Assert.Equal(3178.6, s.Width, 1);
        Assert.Equal(4493.9, s.Height, 1);
    }

    [Fact]
    public void B5PortraitPaperSize()
    {
        var s = PrintLayoutCalculator.GetPaperSizeDip(PrintPaperSize.B5, PrintOrientation.Portrait);
        Assert.Equal(665.2, s.Width, 1);
        Assert.Equal(944.9, s.Height, 1);
    }

    [Fact]
    public void A6LandscapePaperSize()
    {
        var s = PrintLayoutCalculator.GetPaperSizeDip(PrintPaperSize.A6, PrintOrientation.Landscape);
        Assert.Equal(559.4, s.Width, 1);
        Assert.Equal(396.9, s.Height, 1);
    }

    [Fact]
    public void AllPaperSizesHavePositiveDimensions()
    {
        foreach (var paper in Enum.GetValues<PrintPaperSize>())
        {
            var s = PrintLayoutCalculator.GetPaperSizeDip(paper, PrintOrientation.Portrait);
            Assert.True(s.Width > 0, $"{paper} width must be positive");
            Assert.True(s.Height > 0, $"{paper} height must be positive");
        }
    }

    [Fact]
    public void PrintableAreaWith10mmMargins()
    {
        var paper = PrintLayoutCalculator.GetPaperSizeDip(PrintPaperSize.A4, PrintOrientation.Portrait);
        var r = PrintLayoutCalculator.GetPrintableArea(paper, new PrintMargins(10, 10, 10, 10));
        Assert.Equal(718.1, r.Width, 1);
        Assert.Equal(1046.9, r.Height, 1);
        Assert.Equal(37.8, r.X, 1);
        Assert.Equal(37.8, r.Y, 1);
    }

    [Fact]
    public void PrintableAreaClampsNegativeWidth()
    {
        var paper = PrintLayoutCalculator.GetPaperSizeDip(PrintPaperSize.A4, PrintOrientation.Portrait);
        var r = PrintLayoutCalculator.GetPrintableArea(paper, new PrintMargins(300, 10, 10, 10));
        Assert.Equal(0, r.Width, 1);
    }

    [Fact]
    public void NegativeMarginTreatedAsZero()
    {
        var paper = PrintLayoutCalculator.GetPaperSizeDip(PrintPaperSize.A4, PrintOrientation.Portrait);
        var r = PrintLayoutCalculator.GetPrintableArea(paper, new PrintMargins(-5, 0, 0, 0));
        Assert.Equal(0, r.X, 1);
        Assert.Equal(paper.Width, r.Width, 1);
    }

    [Fact]
    public void FitToPageLargeImage()
    {
        var area = new Rect(0, 0, 800, 600);
        var r = PrintLayoutCalculator.CalculateImageRect(area, new Size(1000, 500), PrintFitMode.FitToPage);
        Assert.Equal(0, r.X, 2);
        Assert.Equal(100, r.Y, 2);
        Assert.Equal(800, r.Width, 2);
        Assert.Equal(400, r.Height, 2);
    }

    [Fact]
    public void FitToPageSmallImageUpscales()
    {
        var area = new Rect(0, 0, 800, 600);
        var r = PrintLayoutCalculator.CalculateImageRect(area, new Size(100, 50), PrintFitMode.FitToPage);
        Assert.Equal(800, r.Width, 2);
        Assert.Equal(400, r.Height, 2);
    }

    [Fact]
    public void ActualSizeSmallImage()
    {
        var area = new Rect(0, 0, 800, 600);
        var r = PrintLayoutCalculator.CalculateImageRect(area, new Size(100, 50), PrintFitMode.ActualSize);
        Assert.Equal(100, r.Width, 2);
        Assert.Equal(50, r.Height, 2);
        Assert.Equal(350, r.X, 2);
        Assert.Equal(275, r.Y, 2);
    }

    [Fact]
    public void ActualSizeLargeImageShrinks()
    {
        var area = new Rect(0, 0, 800, 600);
        var r = PrintLayoutCalculator.CalculateImageRect(area, new Size(1600, 800), PrintFitMode.ActualSize);
        Assert.Equal(800, r.Width, 2);
        Assert.Equal(400, r.Height, 2);
    }

    [Fact]
    public void FillImage()
    {
        var area = new Rect(0, 0, 800, 600);
        var r = PrintLayoutCalculator.CalculateImageRect(area, new Size(1000, 500), PrintFitMode.Fill);
        Assert.Equal(1200, r.Width, 2);
        Assert.Equal(600, r.Height, 2);
        Assert.Equal(-200, r.X, 2);
        Assert.Equal(0, r.Y, 2);
    }

    [Fact]
    public void NaturalSizeDip()
    {
        var s1 = PrintLayoutCalculator.GetNaturalSizeDip(300, 300, 300, 300);
        Assert.Equal(96, s1.Width, 1);
        var s2 = PrintLayoutCalculator.GetNaturalSizeDip(300, 300, 0, 0);
        Assert.Equal(300, s2.Width, 1);
    }

    [Fact]
    public void ZeroDimensionReturnsEmpty()
    {
        var area = new Rect(0, 0, 800, 600);
        var r1 = PrintLayoutCalculator.CalculateImageRect(area, new Size(0, 500), PrintFitMode.FitToPage);
        Assert.True(r1.IsEmpty);
        var r2 = PrintLayoutCalculator.CalculateImageRect(new Rect(0, 0, 0, 600), new Size(1000, 500), PrintFitMode.FitToPage);
        Assert.True(r2.IsEmpty);
    }

    [Fact]
    public void CalculateIntegration()
    {
        var natural = new Size(1000, 500);
        var layout = PrintLayoutCalculator.Calculate(PrintPaperSize.A4, PrintOrientation.Portrait, new PrintMargins(10, 10, 10, 10), PrintFitMode.FitToPage, natural);
        Assert.Equal(layout.PrintableAreaDip.Width, layout.ImageRectDip.Width, 1);
        Assert.Equal(718.1, layout.PrintableAreaDip.Width, 1);
    }
}
