using System.Windows;
using System.Windows.Controls;

namespace WcImageViewer.Controls;

public partial class TitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(TitleBar),
        new PropertyMetadata(string.Empty));

    public TitleBar()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Window.GetWindow(this)?.Close();
    }
}
