using System.IO;
using System.Windows;
using System.Windows.Threading;
using WcImageViewer.Services;

namespace WcImageViewer;

public partial class RenameWindow : Window
{
    private readonly string _extension;

    public RenameWindow(string currentName)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowCornerService.Apply(this);
        NameBox.Text = currentName;
        _extension = Path.GetExtension(currentName);
        Loaded += (_, _) => NameBox.Focus();
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => NameBox.Select(0, currentName.Length - _extension.Length)));
    }

    public string NewName { get; private set; } = string.Empty;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "파일 이름을 입력해 주세요.", "이름 바꾸기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "파일 이름에 사용할 수 없는 문자가 있습니다.", "이름 바꾸기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newExtension = Path.GetExtension(name);
        if (!string.Equals(newExtension, _extension, StringComparison.OrdinalIgnoreCase))
        {
            var message = newExtension.Length == 0
                ? $"확장자({_extension})가 없어집니다.\n이 이름으로 바꾸시겠습니까?"
                : $"확장자가 {_extension}에서 {newExtension}(으)로 바뀝니다.\n이 이름으로 바꾸시겠습니까?";
            if (MessageBox.Show(this, message, "이름 바꾸기", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        NewName = name;
        DialogResult = true;
    }
}
