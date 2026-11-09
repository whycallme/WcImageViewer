using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class SaveAsWindow : Window
{
    private readonly BitmapSource _source;
    private readonly IReadOnlyList<(BitmapSource Frame, int DelayMs)> _frames;
    private CancellationTokenSource? _saveCts;
    private bool _isSaving;
    private bool _closeRequested;
    private bool _allowClose;

    public SaveAsWindow(
        BitmapSource source,
        string suggestedFileName,
        string initialDirectory,
        IReadOnlyList<(BitmapSource Frame, int DelayMs)>? frames = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _frames = frames is { Count: > 0 }
            ? frames
            : new[] { (source, 0) };

        InitializeComponent();
        SourceInitialized += (_, _) => WindowCornerService.Apply(this);

        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "image"
            : Path.GetFileName(suggestedFileName.Trim());
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(nameWithoutExtension))
        {
            nameWithoutExtension = "image";
        }

        FolderBox.Text = string.IsNullOrWhiteSpace(initialDirectory)
            ? Environment.CurrentDirectory
            : initialDirectory;
        NameBox.Text = nameWithoutExtension;

        FormatBox.ItemsSource = SaveFormatCatalog.All;
        var defaultFormat = SaveFormatCatalog.ByExtension(Path.GetExtension(fileName))
                            ?? SaveFormatCatalog.Default;
        FormatBox.SelectedItem = defaultFormat;

        UpdateOptionPanels();
        Loaded += (_, _) => NameBox.Focus();
        Closing += OnClosing;
    }

    public string? SavedPath { get; private set; }

    private SaveFormatDefinition SelectedFormat =>
        FormatBox.SelectedItem as SaveFormatDefinition ?? SaveFormatCatalog.Default;

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var format = SelectedFormat;
        var dialog = new SaveFileDialog
        {
            Title = "다른 이름으로 저장",
            FileName = (string.IsNullOrWhiteSpace(NameBox.Text)
                ? "image"
                : NameBox.Text.Trim()) + format.Extension,
            DefaultExt = format.Extension.TrimStart('.'),
            AddExtension = true,
            Filter = $"{format.DisplayName} 이미지|*{format.Extension}",
        };

        if (!string.IsNullOrWhiteSpace(FolderBox.Text) && Directory.Exists(FolderBox.Text))
        {
            dialog.InitialDirectory = FolderBox.Text;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var directory = Path.GetDirectoryName(dialog.FileName);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            FolderBox.Text = directory;
        }

        NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);

        var dialogFormat = SaveFormatCatalog.ByExtension(Path.GetExtension(dialog.FileName));
        if (dialogFormat is not null)
        {
            FormatBox.SelectedItem = dialogFormat;
        }
    }

    private void OnFormatSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOptionPanels();
    }

    private void OnJpegQualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (JpegQualityValue is null) return;
        JpegQualityValue.Text = ((int)Math.Round(e.NewValue)).ToString();
    }

    private void OnWebPQualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WebPQualityValue is null) return;
        WebPQualityValue.Text = ((int)Math.Round(e.NewValue)).ToString();
    }

    private void UpdateOptionPanels()
    {
        var kind = SelectedFormat.OptionKind;
        JpegPanel.Visibility = kind == SaveFormatOptionKind.JpegQuality
            ? Visibility.Visible
            : Visibility.Collapsed;
        WebPPanel.Visibility = kind == SaveFormatOptionKind.WebPQuality
            ? Visibility.Visible
            : Visibility.Collapsed;
        TiffPanel.Visibility = kind == SaveFormatOptionKind.TiffCompression
            ? Visibility.Visible
            : Visibility.Collapsed;
        PngPanel.Visibility = kind == SaveFormatOptionKind.PngInterlace
            ? Visibility.Visible
            : Visibility.Collapsed;
        var isMulti = _frames.Count > 1;
        AnimationPanel.Visibility = isMulti ? Visibility.Visible : Visibility.Collapsed;
        AnimationKeepCheck.IsEnabled = SelectedFormat.SupportsAnimation;
        GifColorNote.Visibility = SelectedFormat.Format == SaveImageFormat.Gif
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private TiffSaveCompression ReadTiffCompression()
    {
        if (TiffCompressionBox.SelectedItem is ComboBoxItem item &&
            item.Content is string text)
        {
            return text switch
            {
                "LZW" => TiffSaveCompression.Lzw,
                "RLE" => TiffSaveCompression.Rle,
                "Zip" => TiffSaveCompression.Zip,
                _ => TiffSaveCompression.None,
            };
        }

        return TiffSaveCompression.None;
    }

    private void OnSaveCancelClick(object sender, RoutedEventArgs e)
    {
        if (_isSaving && _saveCts is not null)
        {
            // 저장 중: 창은 닫지 않고 취소 토큰만 취소한다.
            // 실제 종료는 인코딩 스레드가 끝난 뒤 OnSaveClick에서 처리한다.
            _saveCts.Cancel();
            return;
        }

        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_isSaving || _saveCts is null)
        {
            return;
        }

        e.Cancel = true;

        var confirm = MessageBox.Show(
            this,
            "저장을 취소하고 닫을까요?",
            "다른 이름으로 저장",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        // 토큰 취소 후 인코딩 종료를 기다렸다가 OnSaveClick에서 닫는다.
        _closeRequested = true;
        _saveCts.Cancel();
    }

    private static BitmapSource CreateFrozenCopy(BitmapSource source)
    {
        var copy = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        copy.Freeze();
        return copy;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var folder = FolderBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "파일 이름을 입력해 주세요.", "다른 이름으로 저장", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "파일 이름에 사용할 수 없는 문자가 있습니다.", "다른 이름으로 저장", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show(this, "저장 폴더를 입력해 주세요.", "다른 이름으로 저장", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var format = SelectedFormat;
        var keepAnimation = _frames.Count > 1
                            && format.SupportsAnimation
                            && AnimationKeepCheck.IsChecked == true;
        if (_frames.Count > 1 && !keepAnimation)
        {
            var confirm = MessageBox.Show(
                this,
                $"애니메이션 {_frames.Count}프레임이 단일 이미지(현재 프레임)로 저장됩니다. 계속하시겠습니까?",
                "다른 이름으로 저장",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
            {
                return;
            }
        }

        var fullPath = Path.Combine(folder, name + format.Extension);

        if (File.Exists(fullPath))
        {
            var result = MessageBox.Show(
                this,
                "이미 같은 이름의 파일이 있습니다.\n덮어쓰시겠습니까?",
                "다른 이름으로 저장",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var options = new ImageSaveOptions
        {
            JpegQuality = (int)Math.Round(JpegQualitySlider.Value),
            WebPQuality = (int)Math.Round(WebPQualitySlider.Value),
            WebPLossless = WebPLosslessCheck.IsChecked == true,
            TiffCompression = ReadTiffCompression(),
            PngInterlace = PngInterlaceCheck.IsChecked == true,
        };

        var estimatedFrameCount = keepAnimation ? _frames.Count : 1;
        var estimatedPixelCount = (long)_source.PixelWidth * _source.PixelHeight;
        if (SaveFormatCatalog.ExceedsLongSaveThreshold(estimatedPixelCount, estimatedFrameCount, format, options))
        {
            var estimatedSeconds = (int)Math.Ceiling(
                SaveFormatCatalog.EstimateSaveSeconds(estimatedPixelCount, estimatedFrameCount, format, options));
            var longSaveConfirm = MessageBox.Show(
                this,
                $"이 저장은 약 {estimatedSeconds}초 이상 걸릴 수 있습니다 (용량 {_source.PixelWidth}×{_source.PixelHeight}, 프레임 {estimatedFrameCount}, 형식 {format.DisplayName}). 추정 시간입니다. 계속하시겠습니까?",
                "다른 이름으로 저장",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (longSaveConfirm != MessageBoxResult.OK)
            {
                return;
            }
        }

        _saveCts = new CancellationTokenSource();
        _isSaving = true;
        _closeRequested = false;
        _allowClose = false;
        var cancellationToken = _saveCts.Token;
        if (SaveProgressPanel is not null)
        {
            SaveProgressPanel.Visibility = Visibility.Visible;
        }

        if (SaveButton is not null)
        {
            SaveButton.IsEnabled = false;
        }

        try
        {
            var frozenSource = CreateFrozenCopy(_source);
            var frozenFrames = _frames
                .Select(f => (CreateFrozenCopy(f.Frame), f.DelayMs))
                .ToList();
            var optionsCopy = new ImageSaveOptions
            {
                JpegQuality = options.JpegQuality,
                WebPQuality = options.WebPQuality,
                WebPLossless = options.WebPLossless,
                TiffCompression = options.TiffCompression,
                PngInterlace = options.PngInterlace,
            };

            await Task.Run(() =>
            {
                if (keepAnimation)
                {
                    ImageEncoderService.SaveFrames(frozenFrames, format, optionsCopy, fullPath, cancellationToken);
                }
                else
                {
                    ImageEncoderService.Save(frozenSource, format, optionsCopy, fullPath, cancellationToken);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // ImageEncoderService가 이미 불완전 파일을 지웠을 수 있으므로
            // 존재 여부만 확인해 DialogResult는 설정하지 않는다.
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch
            {
            }

            if (_closeRequested)
            {
                _allowClose = true;
                Close();
            }

            return;
        }
        catch (Exception ex)
        {
            _closeRequested = false;
            MessageBox.Show(
                this,
                $"이미지를 저장할 수 없습니다.\n{ex.Message}",
                "다른 이름으로 저장",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        finally
        {
            _isSaving = false;

            if (SaveProgressPanel is not null)
            {
                SaveProgressPanel.Visibility = Visibility.Collapsed;
            }

            if (SaveButton is not null)
            {
                SaveButton.IsEnabled = true;
            }

            _saveCts?.Dispose();
            _saveCts = null;
        }

        if (_closeRequested)
        {
            _allowClose = true;
            Close();
            return;
        }

        // 인코딩이 끝난 뒤 창이 이미 닫혔으면 DialogResult를 설정하지 않는다.
        if (!IsLoaded && !IsVisible)
        {
            return;
        }

        SavedPath = fullPath;
        DialogResult = true;
    }
}
