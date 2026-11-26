using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WcImageViewer.Services;

public sealed record PrintJobOptions(string PrinterName, PrintPaperSize Paper, PrintOrientation Orientation, PrintFitMode FitMode, PrintMargins MarginsMm, int Copies);

public static class ImagePrintService
{
    public static IReadOnlyList<string> GetPrinterNames()
    {
        try
        {
            var server = new LocalPrintServer();
            var queues = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections });
            return queues.Select(q => q.Name).OrderBy(n => n).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string? GetDefaultPrinterName()
    {
        try
        {
            return new LocalPrintServer().DefaultPrintQueue?.Name;
        }
        catch
        {
            return null;
        }
    }

    public static PrintPageLayout CalculateLayout(BitmapSource image, PrintJobOptions options)
    {
        var natural = PrintLayoutCalculator.GetNaturalSizeDip(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY);
        return PrintLayoutCalculator.Calculate(options.Paper, options.Orientation, options.MarginsMm, options.FitMode, natural);
    }

    public static void Print(BitmapSource image, PrintJobOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);

        var queue = new LocalPrintServer().GetPrintQueue(options.PrinterName);
        var ticket = queue.DefaultPrintTicket.Clone();

        var portrait = PrintLayoutCalculator.GetPaperSizeDip(options.Paper, PrintOrientation.Portrait);
        PageMediaSizeName mediaName = options.Paper switch
        {
            PrintPaperSize.A0 => PageMediaSizeName.ISOA0,
            PrintPaperSize.A1 => PageMediaSizeName.ISOA1,
            PrintPaperSize.A2 => PageMediaSizeName.ISOA2,
            PrintPaperSize.A3 => PageMediaSizeName.ISOA3,
            PrintPaperSize.A4 => PageMediaSizeName.ISOA4,
            PrintPaperSize.A5 => PageMediaSizeName.ISOA5,
            PrintPaperSize.A6 => PageMediaSizeName.ISOA6,
            PrintPaperSize.B4 => PageMediaSizeName.ISOB4,
            // ISOB5 / ISOB6 are not defined in System.Printing.PageMediaSizeName;
            // ISOB5Extra (B5) and JISB6 (B6) are the available equivalents.
            PrintPaperSize.B5 => PageMediaSizeName.ISOB5Extra,
            PrintPaperSize.B6 => PageMediaSizeName.JISB6,
            PrintPaperSize.Letter => PageMediaSizeName.NorthAmericaLetter,
            PrintPaperSize.Legal => PageMediaSizeName.NorthAmericaLegal,
            _ => PageMediaSizeName.ISOA4,
        };
        ticket.PageMediaSize = new PageMediaSize(mediaName, portrait.Width, portrait.Height);

        ticket.PageOrientation = options.Orientation == PrintOrientation.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        ticket.CopyCount = Math.Clamp(options.Copies, 1, 99);

        var layout = CalculateLayout(image, options);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(new Point(0, 0), layout.PageSizeDip));
            dc.PushClip(new RectangleGeometry(layout.PrintableAreaDip));
            dc.DrawImage(image, layout.ImageRectDip);
            dc.Pop();
        }

        PrintQueue.CreateXpsDocumentWriter(queue).Write(visual, ticket);
    }
}