using System.Windows;

namespace WcImageViewer.Services;

public enum PrintPaperSize { A0, A1, A2, A3, A4, A5, A6, B4, B5, B6, Letter, Legal }

public enum PrintOrientation { Portrait, Landscape }

public enum PrintFitMode { FitToPage, ActualSize, Fill }

public readonly record struct PrintMargins(double LeftMm, double TopMm, double RightMm, double BottomMm);

public readonly record struct PrintPageLayout(Size PageSizeDip, Rect PrintableAreaDip, Rect ImageRectDip);

public static class PrintLayoutCalculator
{
    public const double DipPerInch = 96.0;
    public const double MmPerInch = 25.4;

    private static double MmToDip(double mm) => mm * DipPerInch / MmPerInch;

    public static Size GetPaperSizeDip(PrintPaperSize paper, PrintOrientation orientation)
    {
        double widthMm, heightMm;
        switch (paper)
        {
            case PrintPaperSize.A0: widthMm = 841; heightMm = 1189; break;
            case PrintPaperSize.A1: widthMm = 594; heightMm = 841; break;
            case PrintPaperSize.A2: widthMm = 420; heightMm = 594; break;
            case PrintPaperSize.A3: widthMm = 297; heightMm = 420; break;
            case PrintPaperSize.A4: widthMm = 210; heightMm = 297; break;
            case PrintPaperSize.A5: widthMm = 148; heightMm = 210; break;
            case PrintPaperSize.A6: widthMm = 105; heightMm = 148; break;
            case PrintPaperSize.B4: widthMm = 250; heightMm = 353; break;
            case PrintPaperSize.B5: widthMm = 176; heightMm = 250; break;
            case PrintPaperSize.B6: widthMm = 125; heightMm = 176; break;
            case PrintPaperSize.Letter: widthMm = 215.9; heightMm = 279.4; break;
            case PrintPaperSize.Legal: widthMm = 215.9; heightMm = 355.6; break;
            default: widthMm = 210; heightMm = 297; break;
        }
        double w = widthMm * DipPerInch / MmPerInch;
        double h = heightMm * DipPerInch / MmPerInch;
        if (orientation == PrintOrientation.Landscape)
        {
            (w, h) = (h, w);
        }
        return new Size(w, h);
    }

    public static Rect GetPrintableArea(Size paperDip, PrintMargins marginsMm)
    {
        double left = Math.Max(0, marginsMm.LeftMm);
        double top = Math.Max(0, marginsMm.TopMm);
        double right = Math.Max(0, marginsMm.RightMm);
        double bottom = Math.Max(0, marginsMm.BottomMm);

        double leftDip = MmToDip(left);
        double topDip = MmToDip(top);
        double rightDip = MmToDip(right);
        double bottomDip = MmToDip(bottom);

        double w = paperDip.Width - leftDip - rightDip;
        double h = paperDip.Height - topDip - bottomDip;
        if (w < 0) w = 0;
        if (h < 0) h = 0;
        return new Rect(leftDip, topDip, w, h);
    }

    public static Size GetNaturalSizeDip(int pixelWidth, int pixelHeight, double dpiX, double dpiY)
    {
        double dx = (double.IsNaN(dpiX) || dpiX <= 0) ? DipPerInch : dpiX;
        double dy = (double.IsNaN(dpiY) || dpiY <= 0) ? DipPerInch : dpiY;
        return new Size(pixelWidth * DipPerInch / dx, pixelHeight * DipPerInch / dy);
    }

    public static Rect CalculateImageRect(Rect printableArea, Size naturalSizeDip, PrintFitMode fitMode)
    {
        double areaW = printableArea.Width;
        double areaH = printableArea.Height;
        double imgW = naturalSizeDip.Width;
        double imgH = naturalSizeDip.Height;

        if (imgW <= 0 || imgH <= 0 || areaW <= 0 || areaH <= 0)
        {
            return Rect.Empty;
        }

        double scaleFit = Math.Min(areaW / imgW, areaH / imgH);
        double scaleFill = Math.Max(areaW / imgW, areaH / imgH);

        double scale;
        switch (fitMode)
        {
            case PrintFitMode.FitToPage:
                scale = scaleFit;
                break;
            case PrintFitMode.ActualSize:
                scale = 1.0;
                if (imgW > areaW || imgH > areaH)
                {
                    scale = scaleFit;
                }
                break;
            case PrintFitMode.Fill:
                scale = scaleFill;
                break;
            default:
                scale = scaleFit;
                break;
        }

        double w = imgW * scale;
        double h = imgH * scale;
        double x = printableArea.X + (areaW - w) / 2.0;
        double y = printableArea.Y + (areaH - h) / 2.0;
        return new Rect(x, y, w, h);
    }

    public static PrintPageLayout Calculate(PrintPaperSize paper, PrintOrientation orientation, PrintMargins marginsMm, PrintFitMode fitMode, Size naturalSizeDip)
    {
        Size pageSize = GetPaperSizeDip(paper, orientation);
        Rect printable = GetPrintableArea(pageSize, marginsMm);
        Rect imageRect = CalculateImageRect(printable, naturalSizeDip, fitMode);
        return new PrintPageLayout(pageSize, printable, imageRect);
    }
}
