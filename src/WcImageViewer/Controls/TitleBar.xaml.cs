using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WcImageViewer.Controls;

public partial class TitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(TitleBar),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowWindowButtonsProperty = DependencyProperty.Register(
        nameof(ShowWindowButtons),
        typeof(bool),
        typeof(TitleBar),
        new PropertyMetadata(false));

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ShowWindowButtons
    {
        get => (bool)GetValue(ShowWindowButtonsProperty);
        set => SetValue(ShowWindowButtonsProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var visible = ShowWindowButtons ? Visibility.Visible : Visibility.Collapsed;
        MinimizeButton.Visibility = visible;
        MaximizeButton.Visibility = visible;
        if (!ShowWindowButtons)
        {
            return;
        }

        var window = Window.GetWindow(this);
        if (window is null)
        {
            return;
        }
        window.StateChanged -= OnWindowStateChanged;
        window.StateChanged += OnWindowStateChanged;
        UpdateMaximizeGlyph(window);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (!ShowWindowButtons)
        {
            return;
        }
        var window = Window.GetWindow(this);
        if (window is not null)
        {
            UpdateMaximizeGlyph(window);
        }
    }

    private void UpdateMaximizeGlyph(Window window)
    {
        var maximized = window.WindowState == WindowState.Maximized;
        MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
        var label = maximized ? "이전 크기로 복원" : "최대화";
        MaximizeButton.ToolTip = label;
        AutomationProperties.SetName(MaximizeButton, label);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is not null)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is null)
        {
            return;
        }
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Window.GetWindow(this)?.Close();
    }
}
