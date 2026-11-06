using System.IO;
using System.Windows;
using System.Windows.Threading;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class NewFolderWindow : Window
{
    public NewFolderWindow(string currentName)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowCornerService.Apply(this);
        NameBox.Text = currentName;
        Loaded += (_, _) => NameBox.Focus();
    }

    public string NewName { get; private set; } = string.Empty;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "폴더 이름을 입력해 주세요.", "새 폴더", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "폴더 이름에 사용할 수 없는 문자가 있습니다.", "새 폴더", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NewName = name;
        DialogResult = true;
    }
}
