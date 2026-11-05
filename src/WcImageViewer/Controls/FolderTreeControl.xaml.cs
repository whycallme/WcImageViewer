using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WcImageViewer.Services;

namespace WcImageViewer.Controls;

public partial class FolderTreeControl : UserControl
{
    private static readonly object DummyTag = new();

    private DispatcherTimer? _debounceTimer;
    private string? _pendingSelection;

    // 프로그램이 트리 선택을 바꾸는 동안 FolderSelected 를 올리지 않는다(탐색→트리 선택→탐색 재귀 방지).
    private bool _suppressSelectionNotify;
    // 최신 RevealFolderAsync 호출만 계속 진행하도록 하는 세대 번호.
    private int _revealVersion;

    public FolderTreeControl()
    {
        InitializeComponent();
    }

    public string? SelectedFolder { get; private set; }

    public event EventHandler<string>? FolderSelected;

    public void Initialize(string? startFolder)
    {
        Tree.Items.Clear();
        SelectedFolder = null;
        _pendingSelection = null;
        _debounceTimer?.Stop();
        _revealVersion++;

        var myComputer = new TreeViewItem
        {
            Header = CreateHeader("\uE7F4", "내 PC"),
            Tag = "MY_COMPUTER",
            IsExpanded = true
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var root = drive.RootDirectory.FullName;
            var node = new TreeViewItem
            {
                // \uD45C\uC2DC\uB9CC "\uBCFC\uB968\uC774\uB984 (C:)" \uB85C \uBC14\uAFB8\uACE0, \uC2E4\uC81C \uACBD\uB85C \uAC12(Tag)\uC740 root \uADF8\uB300\uB85C \uB454\uB2E4.
                Header = CreateHeader("\uEDA2", DriveDisplayFormatter.FormatDrive(drive)),
                Tag = root,
            };
            node.Items.Add(CreateDummy());
            node.Expanded += Node_Expanded;
            myComputer.Items.Add(node);
        }

        Tree.Items.Add(myComputer);

        if (!string.IsNullOrWhiteSpace(startFolder))
        {
            _ = RevealFolderAsync(startFolder);
        }
    }

    private static StackPanel CreateHeader(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(icon);
        panel.Children.Add(label);
        return panel;
    }

    private static TreeViewItem CreateDummy() => new TreeViewItem
    {
        Tag = DummyTag,
        Visibility = Visibility.Collapsed,
    };

    private async void Node_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem node)
        {
            await LoadChildrenAsync(node);
        }
    }

    private async Task LoadChildrenAsync(TreeViewItem node)
    {
        if (node.Items.Count != 1) return;
        if (node.Items[0] is not TreeViewItem dummy) return;
        if (!ReferenceEquals(dummy.Tag, DummyTag)) return;

        node.Items.Clear();
        if (node.Tag is not string path) return;

        try
        {
            var dirs = await Task.Run(() => EnumerateDirectories(path));
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(name)) name = dir;
                var child = new TreeViewItem
                {
                    Header = CreateHeader("\uE8B7", name),
                    Tag = dir,
                };
                child.Items.Add(CreateDummy());
                child.Expanded += Node_Expanded;
                node.Items.Add(child);
            }
        }
        catch
        {
            RemoveNode(node);
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string path)
    {
        var list = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var attr = File.GetAttributes(dir);
                    if ((attr & FileAttributes.Hidden) != 0) continue;
                    if ((attr & FileAttributes.System) != 0) continue;
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                    list.Add(dir);
                }
                catch
                {
                    // skip inaccessible
                }
            }
        }
        catch
        {
            // ignore enumeration failure
        }
        list.Sort((a, b) => NaturalStringComparer.Instance.Compare(
            Path.GetFileName(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Path.GetFileName(b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))));
        return list;
    }

    private void RemoveNode(TreeViewItem node)
    {
        if (node.Parent is TreeViewItem parent)
        {
            parent.Items.Remove(node);
        }
        else if (node.Parent is TreeView)
        {
            Tree.Items.Remove(node);
        }
    }

    /// <summary>
    /// 폴더 경로까지 트리 노드를 차례로 펼치고(지연 로딩 노드는 자식 로드 후 펼침) 대상 노드를 선택·BringIntoView 한다.
    /// 프로그램 선택이므로 FolderSelected 는 올리지 않는다. 더 새로운 호출이 오면 이전 호출은 중단된다.
    /// </summary>
    public async Task RevealFolderAsync(string? folder)
    {
        var version = ++_revealVersion;
        if (string.IsNullOrWhiteSpace(folder)) return;

        string full;
        try
        {
            full = Path.GetFullPath(folder);
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(full)) return;

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return;

        var myComputer = FindChild(Tree.Items, "MY_COMPUTER");
        if (myComputer is null) return;

        var chain = FolderTreeNavigation.BuildChain(root, full);
        if (chain.Count == 0) return;

        // chain[0] 은 드라이브 노드, 마지막 항목이 대상 폴더.
        var current = FindChild(myComputer.Items, chain[0]);
        if (current is null) return;

        for (var i = 0; i < chain.Count; i++)
        {
            if (version != _revealVersion) return;

            // 자식 로드를 먼저 끝낸 뒤 펼친다. 펼치기가 먼저면 Node_Expanded 가 로드를 선점해 자식이 아직 없는 상태가 된다.
            await LoadChildrenAsync(current);
            if (version != _revealVersion) return;

            current.IsExpanded = true;
            if (i == chain.Count - 1) break;

            var next = FindChild(current.Items, chain[i + 1]);
            if (next is null) return;
            current = next;
        }

        if (version != _revealVersion) return;

        _suppressSelectionNotify = true;
        try
        {
            _debounceTimer?.Stop();
            _pendingSelection = null;
            current.IsSelected = true;
        }
        finally
        {
            _suppressSelectionNotify = false;
        }

        current.BringIntoView();
    }

    private static TreeViewItem? FindChild(ItemCollection items, string path)
    {
        var nodes = items.OfType<TreeViewItem>().ToList();
        var index = FolderTreeNavigation.IndexOfChild(
            nodes.Select(n => n.Tag as string ?? string.Empty).ToList(), path);
        return index >= 0 ? nodes[index] : null;
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem node && node.Tag is string path)
        {
            SelectedFolder = path;
            if (_suppressSelectionNotify)
            {
                return;
            }

            ScheduleDebounce(path);
        }
    }

    private void ScheduleDebounce(string path)
    {
        _pendingSelection = path;
        if (_debounceTimer is null)
        {
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                if (_pendingSelection is not null)
                {
                    FolderSelected?.Invoke(this, _pendingSelection);
                }
            };
        }
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }
}