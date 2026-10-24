using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class UpdateWindow : Window
{
    private readonly UpdateRelease _release;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _downloadCts;
    private UpdatePackage? _package;
    private bool _busy;
    private bool _closed;
    private bool _completed;

    public UpdateWindow(UpdateRelease release)
    {
        _release = release;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowCornerService.Apply(this);

        var nextVersion = UpdateService.ParseTagVersion(release.Tag);
        var nextText = nextVersion is null
            ? release.Tag
            : UpdateService.FormatVersion(nextVersion);
        var tag = string.IsNullOrWhiteSpace(release.Tag) ? nextText : release.Tag.Trim();
        var title = string.IsNullOrWhiteSpace(release.Name) ? $"새 버전 {tag}" : release.Name.Trim();

        HeaderText.Text = title;
        VersionDetailText.Text =
            $"현재 {AppVersion.Display} → {nextText}   ·   {AppVersion.Flavor}   ·   다운로드 {FormatSize(release.AssetSize)}";
        var notes = ReleaseNotesFormatter.ToPlainText(release.Body);
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(notes)
            ? "릴리즈 노트가 없습니다."
            : notes;
        ReleasePageButton.IsEnabled = !string.IsNullOrWhiteSpace(release.HtmlUrl);
        StatusText.Text = "업데이트를 다운로드하고 적용합니다.";
        Loaded += (_, _) => UpdateButton.Focus();
    }

    public bool SkipVersion { get; private set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closed = true;
        _downloadCts?.Cancel();
        _lifetime.Cancel();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        await RunUpdateAsync();
    }

    private async Task RunUpdateAsync()
    {
        _busy = true;
        _completed = false;
        HideError();
        SetBusyState();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _downloadCts = cts;

        try
        {
            var progress = new Progress<double>(ReportProgress);
            var zipPath = await UpdateService.Instance.DownloadAsync(_release, progress, cts.Token);
            if (_closed)
            {
                return;
            }

            StatusText.Text = "업데이트 준비 중...";
            _package = await Task.Run(() => UpdateInstaller.PreparePackage(zipPath, _release));
            if (_closed)
            {
                return;
            }

            StatusText.Text = "업데이트 프로그램을 시작하는 중...";
            UpdateInstaller.Launch(_package);
            _completed = true;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            if (!_closed)
            {
                SetReadyState("다운로드를 취소했습니다.");
            }
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                ShowErrorState(ex);
            }
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
            if (!_completed)
            {
                _busy = false;
            }
        }
    }

    private void ReportProgress(double value)
    {
        if (_closed)
        {
            return;
        }

        var percent = Math.Clamp(value, 0, 1) * 100;
        DownloadProgress.Value = percent;
        PercentText.Text = $"{percent:0}%";
        if (percent < 100)
        {
            StatusText.Text = "다운로드 중...";
        }
    }

    private void SetBusyState()
    {
        UpdateButton.Content = "업데이트 중...";
        UpdateButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        ExitButton.IsEnabled = false;
        ReleasePageButton.IsEnabled = false;
        DownloadProgress.Value = 0;
        DownloadProgress.Visibility = Visibility.Visible;
        PercentText.Text = "0%";
        CancelButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Visible;
        StatusText.Text = "다운로드 준비 중...";
        HintText.Text = "다운로드가 끝나면 앱을 종료하고 업데이트를 적용합니다.";
    }

    private void SetReadyState(string status)
    {
        UpdateButton.Content = "업데이트하고 종료";
        UpdateButton.IsEnabled = true;
        SkipButton.Visibility = Visibility.Visible;
        SkipButton.IsEnabled = true;
        ExitButton.IsEnabled = true;
        ReleasePageButton.IsEnabled = !string.IsNullOrWhiteSpace(_release.HtmlUrl);
        DownloadProgress.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        PercentText.Text = string.Empty;
        StatusText.Text = status;
        HintText.Text = "업데이트를 적용하면 앱이 종료되고 새 버전이 시작됩니다.";
    }

    private void ShowErrorState(Exception exception)
    {
        var logPath = WriteErrorLog(exception);
        ErrorText.Text = exception.Message;
        LogPathText.Text = string.IsNullOrEmpty(logPath)
            ? string.Empty
            : $"로그: {logPath}";
        ErrorPanel.Visibility = Visibility.Visible;
        DownloadProgress.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        PercentText.Text = string.Empty;
        StatusText.Text = "업데이트를 적용하지 못했습니다.";
        UpdateButton.Content = "다시 시도";
        UpdateButton.IsEnabled = true;
        SkipButton.Visibility = Visibility.Collapsed;
        ExitButton.IsEnabled = true;
        ReleasePageButton.IsEnabled = !string.IsNullOrWhiteSpace(_release.HtmlUrl);
        HintText.Text = "그냥 종료하면 현재 버전이 그대로 유지됩니다.";
    }

    private void HideError()
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
        LogPathText.Text = string.Empty;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (!_busy || _downloadCts is null)
        {
            return;
        }

        CancelButton.IsEnabled = false;
        StatusText.Text = "취소 중...";
        _downloadCts.Cancel();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        DialogResult = false;
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        SkipVersion = true;
        DialogResult = false;
    }

    private void OnReleasePageClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_release.HtmlUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_release.HtmlUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"릴리즈 페이지를 열 수 없습니다.\n{ex.Message}",
                "업데이트",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private string WriteErrorLog(Exception exception)
    {
        var path = _package?.LogPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(
                UpdateInstaller.WorkRoot,
                "logs",
                $"update-error-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {exception}{Environment.NewLine}");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _package?.LogPath ?? string.Empty;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30)
        {
            return $"{bytes / (double)(1L << 30):0.##} GB";
        }

        if (bytes >= 1L << 20)
        {
            return $"{bytes / (double)(1L << 20):0.##} MB";
        }

        if (bytes >= 1L << 10)
        {
            return $"{bytes / (double)(1L << 10):0.#} KB";
        }

        return $"{bytes} B";
    }
}
