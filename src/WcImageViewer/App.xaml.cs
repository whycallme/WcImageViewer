using System.IO;
using System.Windows;
using System.Windows.Threading;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _ = Task.Run(() => ArchiveCache.CleanupOld(TimeSpan.FromDays(7)));

        var settingsService = SettingsService.LoadDefault();

        if (settingsService.Settings.SingleWindowMode)
        {
            _singleInstance = SingleInstanceService.Create();
            if (!_singleInstance.IsPrimary)
            {
                var path = e.Args.Length > 0 ? e.Args[0] : null;
                var sent = Task.Run(() => SingleInstanceService
                        .SendPathAsync(path, TimeSpan.FromSeconds(3)))
                    .GetAwaiter()
                    .GetResult();

                if (!sent)
                {
                    MessageBox.Show(
                        "이미 실행 중인 WC Image Viewer 창에 연결할 수 없습니다.",
                        "WC Image Viewer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown();
                return;
            }
        }

        RefreshFileAssociations(settingsService.Settings);

        var window = new MainWindow(settingsService);
        MainWindow = window;
        window.Show();

        if (_singleInstance is not null)
        {
            _singleInstance.StartServer(path =>
            {
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }

                    window.Activate();

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        window.OpenPath(path);
                    }
                }));
            });
        }

        if (e.Args.Length > 0)
        {
            window.OpenPath(e.Args[0]);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private static void RefreshFileAssociations(AppSettings settings)
    {
        if (settings.AssociatedExtensions.Count == 0)
        {
            return;
        }

        try
        {
            var executablePath = FileAssociationService.GetExecutablePath();
            if (!string.IsNullOrEmpty(executablePath))
            {
                FileAssociationService.Apply(settings.AssociatedExtensions, executablePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException or ArgumentException
                                       or InvalidOperationException)
        {
            // Association refresh is best-effort; the settings dialog reports failures.
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "WC Image Viewer",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
