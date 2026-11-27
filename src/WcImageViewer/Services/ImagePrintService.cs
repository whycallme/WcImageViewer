using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
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
        // X/Y DPI 가 다른 이미지도 픽셀 비율을 유지하도록 DpiX 하나로 양축 환산
        var dpi = image.DpiX;
        var natural = PrintLayoutCalculator.GetNaturalSizeDip(image.PixelWidth, image.PixelHeight, dpi, dpi);
        return PrintLayoutCalculator.Calculate(options.Paper, options.Orientation, options.MarginsMm, options.FitMode, natural);
    }

    private static (PrintQueue queue, PrintTicket ticket) BuildTicket(PrintJobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

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

        return (queue, ticket);
    }

    public static void PrintMany(IReadOnlyList<BitmapSource> images, PrintJobOptions options)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("인쇄할 이미지가 없습니다.", nameof(images));
        }
        ArgumentNullException.ThrowIfNull(options);

        var (queue, ticket) = BuildTicket(options);
        var document = BuildDocument(images, options);

        PrintQueue.CreateXpsDocumentWriter(queue).Write(document, ticket);
    }

    public static FixedDocument BuildDocument(IReadOnlyList<BitmapSource> images, PrintJobOptions options)
    {
        var document = new FixedDocument();

        foreach (var image in images)
        {
            ArgumentNullException.ThrowIfNull(image);

            var layout = CalculateLayout(image, options);
            var fixedPage = new FixedPage
            {
                Width = layout.PageSizeDip.Width,
                Height = layout.PageSizeDip.Height,
            };

            var canvas = new Canvas
            {
                Width = layout.PageSizeDip.Width,
                Height = layout.PageSizeDip.Height,
                Background = Brushes.White,
                Clip = new RectangleGeometry(layout.PrintableAreaDip),
            };
            var picture = new Image
            {
                Source = ToUniqueBitmap(image),
                Width = layout.ImageRectDip.Width,
                Height = layout.ImageRectDip.Height,
                Stretch = Stretch.Fill,
            };
            Canvas.SetLeft(picture, layout.ImageRectDip.X);
            Canvas.SetTop(picture, layout.ImageRectDip.Y);
            canvas.Children.Add(picture);
            fixedPage.Children.Add(canvas);

            fixedPage.Measure(layout.PageSizeDip);
            fixedPage.Arrange(new Rect(layout.PageSizeDip));
            fixedPage.UpdateLayout();

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(fixedPage);
            document.Pages.Add(pageContent);
        }

        return document;
    }

    private static BitmapSource ToUniqueBitmap(BitmapSource source)
    {
        // XPS 직렬화기가 파일에서 디코딩된 서로 다른 비트맵을 같은 리소스로 합치는 문제 회피
        var copy = new WriteableBitmap(source);
        copy.Freeze();
        return copy;
    }
}