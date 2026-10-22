using System.Windows.Input;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class ShortcutServiceTests
{
    [Fact]
    public void UsesRequestedDefaults()
    {
        var shortcuts = ShortcutService.GetEffective(null);

        Assert.Equal("F3", shortcuts["Open"]);
        Assert.Equal("F4", shortcuts["CloseImage"]);
        Assert.Equal("F5|Ctrl+Comma", shortcuts["Settings"]);
        Assert.Equal("F6", shortcuts["Bookmarks"]);
        Assert.Equal("F11", shortcuts["Fullscreen"]);
        Assert.Equal("F12", shortcuts["SaveAs"]);
    }

    [Fact]
    public void FormatsAndParsesModifiedGesture()
    {
        var gesture = ShortcutService.Format(Key.S, ModifierKeys.Control | ModifierKeys.Shift);

        Assert.Equal("Ctrl+Shift+S", gesture);
        Assert.True(ShortcutService.TryParse(gesture, out var key, out var modifiers));
        Assert.Equal(Key.S, key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, modifiers);
    }

    [Fact]
    public void CustomGestureOverridesDefault()
    {
        var shortcuts = ShortcutService.GetEffective(new Dictionary<string, string>
        {
            ["Open"] = "Ctrl+O"
        });

        Assert.Equal("Ctrl+O", shortcuts["Open"]);
        Assert.Equal("F4", shortcuts["CloseImage"]);
    }
}
