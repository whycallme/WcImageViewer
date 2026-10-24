using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class BookmarkWindow : Window
{
    private readonly ObservableCollection<string> _bookmarks;
    private readonly string? _currentPath;

    public BookmarkWindow(IEnumerable<string> bookmarks, string? currentPath)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowCornerService.Apply(this);

        _bookmarks = new ObservableCollection<string>(
            bookmarks
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        _currentPath = currentPath;

        BookmarkList.ItemsSource = _bookmarks;
        AddCurrentButton.IsEnabled = !string.IsNullOrWhiteSpace(currentPath);
    }

    public IReadOnlyList<string> Bookmarks => _bookmarks.ToArray();

    public string? SelectedPath { get; private set; }

    private void OnAddCurrentClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            return;
        }

        if (!_bookmarks.Any(path => string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase)))
        {
            _bookmarks.Add(_currentPath);
        }

        BookmarkList.SelectedItem = _currentPath;
        BookmarkList.ScrollIntoView(_currentPath);
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is not string path)
        {
            return;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            MessageBox.Show(
                this,
                "책갈피 대상이 존재하지 않습니다.",
                "책갈피",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SelectedPath = path;
        DialogResult = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is string path)
        {
            _bookmarks.Remove(path);
        }
    }
}
