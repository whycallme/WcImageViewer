using System.Windows.Input;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class ShortcutServiceTests
{
    [Fact]
    public void UsesRequestedDefaults()
    {
        var shortcuts = ShortcutService.GetEffective(null);

        Assert.Equal("F3|Ctrl+O", shortcuts["Open"]);
        Assert.Equal("F4", shortcuts["CloseImage"]);
        Assert.Equal("F5|Ctrl+Comma", shortcuts["Settings"]);
        Assert.Equal("F6", shortcuts["Bookmarks"]);
        Assert.Equal("F11", shortcuts["Fullscreen"]);
        Assert.Equal("F12", shortcuts["SaveAs"]);
    }

    [Fact]
    public void UpDownAreDefaultPreviousAndNextKeys()
    {
        var shortcuts = ShortcutService.GetEffective(null);

        Assert.Contains("Up", ShortcutService.SplitGestures(shortcuts["Previous"]));
        Assert.Contains("Down", ShortcutService.SplitGestures(shortcuts["Next"]));
        Assert.Contains("Left", ShortcutService.SplitGestures(shortcuts["Previous"]));
        Assert.Contains("Right", ShortcutService.SplitGestures(shortcuts["Next"]));
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

    [Fact]
    public void OpenDefaultMatchesF3AndCtrlO()
    {
        var shortcuts = ShortcutService.GetEffective(null);
        var gestures = ShortcutService.SplitGestures(shortcuts["Open"])
            .Select(g => ShortcutService.Normalize(g))
            .ToArray();

        Assert.Contains("F3", gestures);
        Assert.Contains("Ctrl+O", gestures);
    }

    [Fact]
    public void NoDuplicateGesturesAcrossDefinitions()
    {
        var gestures = ShortcutService.Definitions
            .SelectMany(d => ShortcutService.SplitGestures(d.DefaultGesture))
            .Select(g => ShortcutService.Normalize(g))
            .ToList();

        Assert.Equal(gestures.Count, gestures.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void LegacyDefaultOpenExpandsToF3AndCtrlO()
    {
        var shortcuts = ShortcutService.GetEffective(new Dictionary<string, string>
        {
            ["Open"] = "F3"
        });

        var gestures = ShortcutService.SplitGestures(shortcuts["Open"])
            .Select(g => ShortcutService.Normalize(g))
            .ToArray();

        Assert.Contains("F3", gestures);
        Assert.Contains("Ctrl+O", gestures);
    }

    [Fact]
    public void UserCustomOpenIsRespected()
    {
        var shortcuts = ShortcutService.GetEffective(new Dictionary<string, string>
        {
            ["Open"] = "F4"
        });

        Assert.Equal("F4", shortcuts["Open"]);
    }

    [Fact]
    public void ToCustomMapOmitsDefaults()
    {
        var effective = new Dictionary<string, string>
        {
            ["Open"] = "F3|Ctrl+O",
            ["CloseImage"] = "F5"
        };

        var map = ShortcutService.ToCustomMap(effective);

        Assert.False(map.ContainsKey("Open"));
        Assert.Equal("F5", map["CloseImage"]);
    }
}
