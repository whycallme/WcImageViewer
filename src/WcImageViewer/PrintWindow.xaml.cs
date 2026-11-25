using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class PrintWindow : Window
{
    private readonly BitmapSource _image;

    public PrintWindow(BitmapSource image, string documentName)
    {
        ArgumentNullException.ThrowIfNull(image);
        _image = image;

        InitializeComponent();
        SourceInitialized += (_, _) => WindowCornerService.Apply(this);

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        DocumentNameText.Text = documentName;

        PrinterBox.ItemsSource = ImagePrintService.GetPrinterNames();
        var defaultPrinter = ImagePrintService.GetDefaultPrinterName();
        if (defaultPrinter is not null && PrinterBox.Items.Contains(defaultPrinter))
        {
            PrinterBox.SelectedItem = defaultPrinter;
        }

        PrinterBox.SelectionChanged += (_, _) => Refresh();
        PaperBox.SelectionChanged += (_, _) => Refresh();
        FitBox.SelectionChanged += (_, _) => Refresh();
        PortraitRadio.Checked += (_, _) => Refresh();
        LandscapeRadio.Checked += (_, _) => Refresh();
        MarginLeftBox.TextChanged += (_, _) => Refresh();
        MarginTopBox.TextChanged += (_, _) => Refresh();
        MarginRightBox.TextChanged += (_, _) => Refresh();
        MarginBottomBox.TextChanged += (_, _) => Refresh();
        CopiesBox.TextChanged += (_, _) => Refresh();

        Loaded += (_, _) => Refresh();

        Refresh();
    }

    private PrintJobOptions? BuildOptions(out string? error)
    {
        error = null;

        if (PrinterBox.SelectedItem is not string printerName || string.IsNullOrWhiteSpace(printerName))
        {
            error = PrinterBox.Items.Count == 0
                ? "사용 가능한 프린터가 없습니다."
                : "프린터를 선택하세요.";
            return null;
        }

        if (PaperBox.SelectedItem is not ComboBoxItem paperItem || paperItem.Tag is not PrintPaperSize paper)
        {
            error = "용지를 선택하세요.";
            return null;
        }

        var orientation = LandscapeRadio.IsChecked == true
            ? PrintOrientation.Landscape
            : PrintOrientation.Portrait;

        if (FitBox.SelectedItem is not ComboBoxItem fitItem || fitItem.Tag is not PrintFitMode fit)
        {
            error = "맞춤 방식을 선택하세요.";
            return null;
        }

        if (!double.TryParse(MarginLeftBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var left) || left < 0 || left > 100)
        {
            error = "여백 값이 올바르지 않습니다. (0~100mm)";
            return null;
        }
        if (!double.TryParse(MarginTopBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var top) || top < 0 || top > 100)
        {
            error = "여백 값이 올바르지 않습니다. (0~100mm)";
            return null;
        }
        if (!double.TryParse(MarginRightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var right) || right < 0 || right > 100)
        {
            error = "여백 값이 올바르지 않습니다. (0~100mm)";
            return null;
        }
        if (!double.TryParse(MarginBottomBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var bottom) || bottom < 0 || bottom > 100)
        {
            error = "여백 값이 올바르지 않습니다. (0~100mm)";
            return null;
        }

        if (!int.TryParse(CopiesBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var copies) || copies < 1 || copies > 99)
        {
            error = "매수는 1~99 사이여야 합니다.";
            return null;
        }

        return new PrintJobOptions(
            printerName,
            paper,
            orientation,
            fit,
            new PrintMargins(left, top, right, bottom),
            copies);
    }

    private void Refresh()
    {
        var options = BuildOptions(out var error);
        if (options is null)
        {
            ErrorText.Text = error ?? string.Empty;
            ErrorText.Visibility = Visibility.Visible;
            PrintButton.IsEnabled = false;
            PreviewCanvas.Children.Clear();
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        PrintButton.IsEnabled = true;
        DrawPreview(options);
    }

    private void DrawPreview(PrintJobOptions options)
    {
        var layout = ImagePrintService.CalculateLayout(_image, options);

        double areaWidth = PreviewBorder.ActualWidth - 16;
        double areaHeight = PreviewBorder.ActualHeight - 16;
        if (areaWidth <= 0 || areaHeight <= 0)
        {
            areaWidth = 380;
            areaHeight = 480;
        }

        double paperW = layout.PageSizeDip.Width;
        double paperH = layout.PageSizeDip.Height;
        double scale = Math.Min(areaWidth / paperW, areaHeight / paperH);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            scale = 1;
        }

        double scaledPaperW = paperW * scale;
        double scaledPaperH = paperH * scale;
        double paperLeft = (PreviewBorder.ActualWidth - scaledPaperW) / 2.0;
        double paperTop = (PreviewBorder.ActualHeight - scaledPaperH) / 2.0;
        if (paperLeft < 0) paperLeft = 0;
        if (paperTop < 0) paperTop = 0;

        PreviewCanvas.Children.Clear();

        var paperRect = new Rectangle
        {
            Width = scaledPaperW,
            Height = scaledPaperH,
            Fill = Brushes.White,
            Stroke = Brushes.Gray,
            StrokeThickness = 1
        };
        Canvas.SetLeft(paperRect, paperLeft);
        Canvas.SetTop(paperRect, paperTop);
        PreviewCanvas.Children.Add(paperRect);

        var printable = layout.PrintableAreaDip;
        var printableRect = new Rectangle
        {
            Width = printable.Width * scale,
            Height = printable.Height * scale,
            Stroke = Brushes.Gray,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };
        Canvas.SetLeft(printableRect, paperLeft + printable.X * scale);
        Canvas.SetTop(printableRect, paperTop + printable.Y * scale);
        PreviewCanvas.Children.Add(printableRect);

        var imageRect = layout.ImageRectDip;
        var img = new Image
        {
            Source = _image,
            Stretch = Stretch.Fill,
            Width = imageRect.Width * scale,
            Height = imageRect.Height * scale
        };

        Canvas.SetLeft(img, paperLeft + imageRect.X * scale);
        Canvas.SetTop(img, paperTop + imageRect.Y * scale);

        img.Clip = new RectangleGeometry(new Rect(
            (printable.X - imageRect.X) * scale,
            (printable.Y - imageRect.Y) * scale,
            printable.Width * scale,
            printable.Height * scale));

        PreviewCanvas.Children.Add(img);
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        var options = BuildOptions(out _);
        if (options is null)
        {
            return;
        }

        Cursor = Cursors.Wait;
        try
        {
            ImagePrintService.Print(_image, options);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"인쇄하지 못했습니다.\n{ex.Message}", "인쇄", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = null;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}