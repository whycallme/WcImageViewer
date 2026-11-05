using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WcImageViewer;
using WcImageViewer.Models;

namespace WcImageViewer.Tests;

public class ListViewWindowSmokeTests
{
    [Fact]
    public void OpenListViewWindow_DoesNotThrow()
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
                    var dict = new ResourceDictionary();
                    dict.Source = new Uri("pack://application:,,,/WcImageViewer;component/Styles/DarkTheme.xaml");
                    app.Resources.MergedDictionaries.Add(dict);
                }
                var folder = TestTemp.CreateFolder();
                var img = Path.Combine(folder, "a.png");
                TestImages.CreatePng(img, 1, 1);

                var settings = new AppSettings();
                var w = new ListViewWindow(settings, () => { }, folder, img);
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
}
