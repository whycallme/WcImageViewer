using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WcImageViewer;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class PrintWindowSmokeTests
{
    [Fact]
    public void OpenPrintWindow_DoesNotThrow()
    {
        Exception? captured = null;
        string? stack = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.UnhandledException += (_, e) =>
                {
                    captured ??= e.Exception;
                    stack ??= e.Exception.ToString();
                    e.Handled = true;
                };

                if (Application.Current is null)
                {
                    var app = new Application();
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var dict = new ResourceDictionary();
                    dict.Source = new Uri("pack://application:,,,/WcImageViewer;component/Styles/DarkTheme.xaml");
                    app.Resources.MergedDictionaries.Add(dict);
                }

                var source = BitmapSource.Create(
                    100, 50, 96, 96, PixelFormats.Bgra32, null,
                    new byte[100 * 50 * 4], 100 * 4);

                var w = new PrintWindow(source, "a.png");
                w.Show();

                var start = DateTime.Now;
                while ((DateTime.Now - start).TotalSeconds < 3)
                {
                    dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                    Thread.Sleep(10);
                }
                w.Close();
                dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            }
            catch (Exception ex)
            {
                captured ??= ex;
                stack ??= ex.ToString();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw new Exception(stack ?? captured.ToString(), captured);
        }
    }

    [Fact]
    public void GetPrinterNamesAndLayout_DoesNotThrow()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var names = ImagePrintService.GetPrinterNames();
                if (names.Count > 0)
                {
                    var image = BitmapSource.Create(
                        100, 50, 96, 96, PixelFormats.Bgra32, null,
                        new byte[100 * 50 * 4], 100 * 4);
                    var options = new PrintJobOptions(
                        names[0], PrintPaperSize.A4, PrintOrientation.Portrait,
                        PrintFitMode.FitToPage, new PrintMargins(10, 10, 10, 10), 1);
                    var layout = ImagePrintService.CalculateLayout(image, options);
                    Assert.True(layout.ImageRectDip.Width > 0);
                    Assert.True(layout.ImageRectDip.Height > 0);
                    Assert.True(layout.ImageRectDip.Right <= layout.PageSizeDip.Width + 1);
                    Assert.True(layout.ImageRectDip.Bottom <= layout.PageSizeDip.Height + 1);
                }
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw new Exception(captured.ToString(), captured);
        }
    }
}
