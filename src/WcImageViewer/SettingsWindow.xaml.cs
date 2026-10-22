using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class SettingsWindow : Window
{
    private static readonly int[] Intervals = { 2, 3, 5, 10, 15, 30 };
    private static readonly (string Label, int Percent)[] ZoomOptions =
    {
        ("창에 맞춤", 0),
        ("25%", 25),
        ("50%", 50),
        ("75%", 75),
        ("100%", 100),
        ("150%", 150),
        ("200%", 200)
    };

    private readonly List<(string Label, int Percent)> _zoomChoices;
    private readonly Dictionary<string, CheckBox> _extensionChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _shortcutBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _shortcutButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _shortcutGestures;
    private string? _capturingActionId;
    private bool _initializing = true;

    public SettingsWindow(
        int slideshowIntervalSeconds,
        bool slideshowRepeat,
        bool slideshowRandom,
        bool thumbnailsVisible,
        int defaultZoomPercent,
        bool autoFitWindowToImage,
        bool confirmDelete,
        bool useRecycleBin,
        string editorPath,
        IReadOnlyCollection<string> associatedExtensions,
        bool singleWindowMode,
        bool wrapNavigation,
        bool showNavigationNotice,
        IReadOnlyDictionary<string, string> shortcuts)
    {
        InitializeComponent();

        var intervals = Intervals.ToList();
        if (!intervals.Contains(slideshowIntervalSeconds))
        {
            intervals.Add(slideshowIntervalSeconds);
            intervals.Sort();
        }

        IntervalBox.ItemsSource = intervals;
        IntervalBox.SelectedItem = slideshowIntervalSeconds;
        SlideshowRepeatCheck.IsChecked = slideshowRepeat;
        SlideshowRandomCheck.IsChecked = slideshowRandom;

        _zoomChoices = ZoomOptions.ToList();
        if (defaultZoomPercent > 0 &&
            !_zoomChoices.Any(option => option.Percent == defaultZoomPercent))
        {
            _zoomChoices.Add(($"{defaultZoomPercent}%", defaultZoomPercent));
            _zoomChoices.Sort((left, right) => left.Percent.CompareTo(right.Percent));
        }

        DefaultZoomBox.ItemsSource = _zoomChoices.Select(option => option.Label).ToArray();
        var zoomIndex = _zoomChoices.FindIndex(option => option.Percent == defaultZoomPercent);
        DefaultZoomBox.SelectedIndex = zoomIndex >= 0 ? zoomIndex : 0;

        ThumbnailsCheck.IsChecked = thumbnailsVisible;
        AutoFitCheck.IsChecked = autoFitWindowToImage;
        ConfirmDeleteCheck.IsChecked = confirmDelete;
        RecycleBinCheck.IsChecked = useRecycleBin;
        EditorPathBox.Text = editorPath;

        BuildExtensionChoices(associatedExtensions);

        WindowModeBox.ItemsSource = new[] { "여러 창으로 열기", "하나의 창으로 열기" };
        WindowModeBox.SelectedIndex = singleWindowMode ? 1 : 0;

        NavigationWrapBox.ItemsSource = new[] { "첫 파일로 가기", "아무것도 안 하기" };
        NavigationWrapBox.SelectedIndex = wrapNavigation ? 0 : 1;
        NavigationNoticeCheck.IsChecked = showNavigationNotice;

        _shortcutGestures = ShortcutService.GetEffective(shortcuts);
        BuildShortcutRows();

        CategoryList.SelectedIndex = 0;
        ShowCategory(0);
        _initializing = false;
    }

    public int IntervalSeconds { get; private set; } = 3;

    public bool SlideshowRepeat { get; private set; } = true;

    public bool SlideshowRandom { get; private set; }

    public bool ShowThumbnails { get; private set; } = true;

    public int DefaultZoomPercent { get; private set; }

    public bool AutoFitWindowToImage { get; private set; } = true;

    public bool ConfirmDelete { get; private set; } = true;

    public bool UseRecycleBin { get; private set; } = true;

    public string EditorPath { get; private set; } = string.Empty;

    public IReadOnlyList<string> SelectedExtensions { get; private set; } = Array.Empty<string>();

    public bool SingleWindowMode { get; private set; }

    public bool WrapNavigation { get; private set; } = true;

    public bool ShowNavigationNotice { get; private set; } = true;

    public IReadOnlyDictionary<string, string> SelectedShortcuts =>
        new Dictionary<string, string>(_shortcutGestures);

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowCategory(CategoryList.SelectedIndex);
    }

    private void ShowCategory(int index)
    {
        if (GeneralPanel is null)
        {
            return;
        }

        GeneralPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        FilePanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        ShortcutPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        AssociationPanel.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildExtensionChoices(IReadOnlyCollection<string> selectedExtensions)
    {
        var foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));

        foreach (var extension in FileAssociationService.SupportedExtensions)
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = FileIconService.Load(extension);
            if (icon is not null)
            {
                content.Children.Add(new Image
                {
                    Source = icon,
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = extension,
                Foreground = foreground,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            var checkBox = new CheckBox
            {
                Content = content,
                Tag = extension,
                IsChecked = selectedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase),
                Foreground = foreground,
                Margin = new Thickness(0, 0, 0, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            _extensionChecks[extension] = checkBox;
            var targetPanel = FileAssociationService.ImageExtensions.Contains(extension)
                ? ImageExtensionPanel
                : ArchiveExtensionPanel;
            targetPanel.Children.Add(checkBox);
        }
    }

    private void BuildShortcutRows()
    {
        foreach (var definition in ShortcutService.Definitions)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = definition.Label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var gestureBox = new TextBox
            {
                Text = ShortcutService.ToDisplay(_shortcutGestures[definition.Id]),
                IsReadOnly = true,
                Height = 26,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2C)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4E)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = ShortcutService.ToDisplay(_shortcutGestures[definition.Id])
            };
            Grid.SetColumn(gestureBox, 1);
            row.Children.Add(gestureBox);
            _shortcutBoxes[definition.Id] = gestureBox;

            var changeButton = new Button
            {
                Content = "변경",
                Tag = definition.Id,
                FontSize = 11,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0)
            };
            changeButton.Click += OnChangeShortcutClick;
            Grid.SetColumn(changeButton, 2);
            row.Children.Add(changeButton);
            _shortcutButtons[definition.Id] = changeButton;

            var resetButton = new Button
            {
                Content = "초기화",
                Tag = definition.Id,
                FontSize = 11,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(4, 0, 0, 0)
            };
            resetButton.Click += OnResetShortcutClick;
            Grid.SetColumn(resetButton, 3);
            row.Children.Add(resetButton);

            ShortcutRowsPanel.Children.Add(row);
        }
    }

    private void OnChangeShortcutClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId })
        {
            return;
        }

        if (_capturingActionId is not null &&
            _shortcutButtons.TryGetValue(_capturingActionId, out var previousButton))
        {
            previousButton.Content = "변경";
        }

        _capturingActionId = actionId;
        _shortcutBoxes[actionId].Text = "키를 누르세요";
        _shortcutButtons[actionId].Content = "입력...";
    }

    private void OnResetShortcutClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId })
        {
            return;
        }

        var definition = ShortcutService.Definitions.First(item => item.Id == actionId);
        _shortcutGestures[actionId] = definition.DefaultGesture;
        _shortcutBoxes[actionId].Text = ShortcutService.ToDisplay(definition.DefaultGesture);
    }

    private void OnPreviewShortcutKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingActionId is null)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _shortcutBoxes[_capturingActionId].Text =
                ShortcutService.ToDisplay(_shortcutGestures[_capturingActionId]);
            _shortcutButtons[_capturingActionId].Content = "변경";
            _capturingActionId = null;
            return;
        }

        if (ShortcutService.IsModifierKey(key))
        {
            return;
        }

        var gesture = ShortcutService.Format(key, Keyboard.Modifiers);
        var conflict = _shortcutGestures.Any(pair =>
            pair.Key != _capturingActionId &&
            ShortcutService.SplitGestures(pair.Value).Any(existing =>
                string.Equals(
                    ShortcutService.Normalize(existing),
                    gesture,
                    StringComparison.OrdinalIgnoreCase)));

        if (conflict)
        {
            MessageBox.Show(
                this,
                "이미 다른 기능에 사용 중인 단축키입니다.",
                "단축키",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _shortcutGestures[_capturingActionId] = gesture;
        _shortcutBoxes[_capturingActionId].Text = gesture;
        _shortcutButtons[_capturingActionId].Content = "변경";
        _capturingActionId = null;
    }

    private void OnDefaultZoomChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        if (DefaultZoomBox.SelectedIndex > 0)
        {
            AutoFitCheck.IsChecked = false;
        }
    }

    private void OnBrowseEditorClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "편집 앱 선택",
            Filter = "실행 파일|*.exe|모든 파일|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            EditorPathBox.Text = dialog.FileName;
        }
    }

    private void OnSelectAllImagesClick(object sender, RoutedEventArgs e)
    {
        SetExtensionChecks(FileAssociationService.ImageExtensions, true);
    }

    private void OnSelectAllArchivesClick(object sender, RoutedEventArgs e)
    {
        SetExtensionChecks(FileAssociationService.ArchiveExtensions, true);
    }

    private void OnClearAllExtensionsClick(object sender, RoutedEventArgs e)
    {
        SetExtensionChecks(FileAssociationService.SupportedExtensions, false);
    }

    private void SetExtensionChecks(IEnumerable<string> extensions, bool isChecked)
    {
        foreach (var extension in extensions)
        {
            if (_extensionChecks.TryGetValue(extension, out var checkBox))
            {
                checkBox.IsChecked = isChecked;
            }
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        IntervalSeconds = IntervalBox.SelectedItem is int interval ? interval : 3;
        SlideshowRepeat = SlideshowRepeatCheck.IsChecked == true;
        SlideshowRandom = SlideshowRandomCheck.IsChecked == true;
        ShowThumbnails = ThumbnailsCheck.IsChecked == true;
        DefaultZoomPercent = DefaultZoomBox.SelectedIndex >= 0
            ? _zoomChoices[DefaultZoomBox.SelectedIndex].Percent
            : 0;
        AutoFitWindowToImage = AutoFitCheck.IsChecked == true;
        ConfirmDelete = ConfirmDeleteCheck.IsChecked == true;
        UseRecycleBin = RecycleBinCheck.IsChecked == true;
        EditorPath = EditorPathBox.Text.Trim();
        SelectedExtensions = _extensionChecks
            .Where(pair => pair.Value.IsChecked == true)
            .Select(pair => pair.Key)
            .ToArray();
        SingleWindowMode = WindowModeBox.SelectedIndex == 1;
        WrapNavigation = NavigationWrapBox.SelectedIndex == 0;
        ShowNavigationNotice = NavigationNoticeCheck.IsChecked == true;
        DialogResult = true;
    }
}
