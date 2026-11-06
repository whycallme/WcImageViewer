using System.Windows.Input;

namespace WcImageViewer.Services;

public sealed record ShortcutDefinition(string Id, string Label, string DefaultGesture);

public static class ShortcutService
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> LegacyDefaults { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["Open"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "F3" }
        };

    public static IReadOnlyList<ShortcutDefinition> Definitions { get; } = new[]
    {
        new ShortcutDefinition("Open", "파일 열기", "F3|Ctrl+O"),
        new ShortcutDefinition("CloseImage", "현재 이미지 닫기", "F4"),
        new ShortcutDefinition("Settings", "설정", "F5|Ctrl+Comma"),
        new ShortcutDefinition("Bookmarks", "책갈피 관리", "F6"),
        new ShortcutDefinition("Fullscreen", "전체화면", "F11"),
        new ShortcutDefinition("SaveAs", "다른 이름으로 저장", "F12"),
        new ShortcutDefinition("Previous", "이전 이미지", "Left|Up|PageUp|Backspace"),
        new ShortcutDefinition("Next", "다음 이미지", "Right|Down|PageDown|Space"),
        new ShortcutDefinition("First", "첫 이미지", "Home"),
        new ShortcutDefinition("Last", "마지막 이미지", "End"),
        new ShortcutDefinition("ZoomIn", "확대", "Add|Plus|Shift+Plus"),
        new ShortcutDefinition("ZoomOut", "축소", "Subtract|Minus"),
        new ShortcutDefinition("Fit", "창에 맞춤", "0|NumPad0"),
        new ShortcutDefinition("ActualSize", "100% 크기", "1|NumPad1"),
        new ShortcutDefinition("WindowFit", "이미지 크기에 창 맞춤", "W"),
        new ShortcutDefinition("Slideshow", "슬라이드쇼 시작·중지", "S"),
        new ShortcutDefinition("Thumbnails", "썸네일 스트립 고정 표시·숨김", "T"),
        new ShortcutDefinition("Info", "이미지 정보 오버레이", "Tab"),
        new ShortcutDefinition("ListView", "리스트뷰 열기", "Enter"),
        new ShortcutDefinition("ParentFolder", "상위 폴더 이동", "Alt+Up"),
        new ShortcutDefinition("Back", "뒤로/이전 이미지", "Alt+Left"),
        new ShortcutDefinition("Forward", "앞으로/다음 이미지", "Alt+Right"),
        new ShortcutDefinition("Rename", "이름 바꾸기", "F2"),
        new ShortcutDefinition("Delete", "파일 삭제", "Delete"),
        new ShortcutDefinition("Copy", "클립보드로 복사", "Ctrl+C"),
        new ShortcutDefinition("Edit", "편집 앱으로 열기", "Ctrl+E"),
        new ShortcutDefinition("CloseWindow", "창 닫기", "Escape"),
        new ShortcutDefinition("NewFolder", "새 폴더", "Ctrl+Shift+N")
    };

    public static Dictionary<string, string> GetEffective(
        IReadOnlyDictionary<string, string>? custom)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var definition in Definitions)
        {
            if (custom is not null &&
                custom.TryGetValue(definition.Id, out var gesture) &&
                IsValidGestureSet(gesture))
            {
                var normalized = Normalize(gesture);
                if (LegacyDefaults.TryGetValue(definition.Id, out var legacy) &&
                    legacy.Contains(normalized))
                {
                    result[definition.Id] = definition.DefaultGesture;
                }
                else
                {
                    result[definition.Id] = normalized;
                }
            }
            else
            {
                result[definition.Id] = definition.DefaultGesture;
            }
        }

        return result;
    }

    public static Dictionary<string, string> ToCustomMap(
        IReadOnlyDictionary<string, string>? effective)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (effective is null)
        {
            return result;
        }

        foreach (var pair in effective)
        {
            var definition = Definitions.FirstOrDefault(item => item.Id == pair.Key);
            if (definition is null)
            {
                result[pair.Key] = pair.Value;
                continue;
            }

            if (string.Equals(
                    Normalize(pair.Value),
                    Normalize(definition.DefaultGesture),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result[pair.Key] = pair.Value;
        }

        return result;
    }

    public enum Screen
    {
        Viewer,
        List
    }

    // 뷰어/리스트 공통 keymap 에서 해당 화면에 구현이 없는 동작.
    private static readonly IReadOnlySet<string> ViewerUnavailable =
        new HashSet<string>(StringComparer.Ordinal) { "ParentFolder" };

    private static readonly IReadOnlySet<string> ListUnavailable =
        new HashSet<string>(StringComparer.Ordinal) { "CloseImage", "WindowFit", "Thumbnails", "Edit" };

    public static bool IsUnavailableIn(string actionId, Screen screen)
    {
        return screen == Screen.Viewer
            ? ViewerUnavailable.Contains(actionId)
            : ListUnavailable.Contains(actionId);
    }

    public static string GetUnavailableMessage(string actionId)
    {
        var label = Definitions.FirstOrDefault(item => item.Id == actionId)?.Label ?? actionId;
        return $"「{label}」 기능은 이 화면에서 사용할 수 없습니다.";
    }

    /// <summary>현재 키 입력에 해당하는 동작 id 를 찾는다. 없으면 null.</summary>
    public static string? FindAction(IReadOnlyDictionary<string, string> shortcuts, KeyEventArgs e)
    {
        foreach (var shortcut in shortcuts)
        {
            if (Matches(shortcut.Value, e))
            {
                return shortcut.Key;
            }
        }

        return null;
    }

    /// <summary>
    /// TextBox 등 입력 필드에 포커스가 있을 때 키를 입력(편집)으로 남겨야 하는지.
    /// F1~F24 와 Alt 조합만 단축키로 통과시킨다.
    /// </summary>
    public static bool IsTextEditingKey(KeyEventArgs e)
    {
        var key = PressedKey(e);
        if (key >= Key.F1 && key <= Key.F24)
        {
            return false;
        }

        return (Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt;
    }

    /// <summary>
    /// 목록(ListBox)이 포커스일 때 목록이 자체적으로 처리해야 하는 무수식 탐색 키인지.
    /// </summary>
    public static bool IsListNavigationKey(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows);
        if (modifiers != ModifierKeys.None)
        {
            return false;
        }

        return PressedKey(e) is Key.Up or Key.Down or Key.Left or Key.Right
            or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Space or Key.Enter;
    }

    private static Key PressedKey(KeyEventArgs e)
    {
        return e.Key == Key.System ? e.SystemKey : e.Key;
    }

    public static bool Matches(string gestureSet, KeyEventArgs e)
    {
        var pressedKey = PressedKey(e);
        var pressedModifiers = Keyboard.Modifiers &
                               (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows);

        foreach (var gesture in SplitGestures(gestureSet))
        {
            if (TryParse(gesture, out var key, out var modifiers) &&
                pressedKey == key &&
                pressedModifiers == modifiers)
            {
                return true;
            }
        }

        return false;
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>(5);

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(key));
        return string.Join("+", parts);
    }

    public static string ToDisplay(string gestureSet)
    {
        return string.Join(" / ", SplitGestures(gestureSet));
    }

    public static IEnumerable<string> SplitGestures(string gestureSet)
    {
        return gestureSet
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part));
    }

    public static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
    }

    public static bool TryParse(string? gesture, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }

        return TryParseKey(parts[^1], out key);
    }

    public static string Normalize(string gestureSet)
    {
        var parts = SplitGestures(gestureSet)
            .Select(gesture => TryParse(gesture, out var key, out var modifiers)
                ? Format(key, modifiers)
                : gesture.Trim())
            .ToArray();

        return string.Join("|", parts);
    }

    public static bool IsValidGestureSet(string? gestureSet)
    {
        if (string.IsNullOrWhiteSpace(gestureSet))
        {
            return false;
        }

        var gestures = SplitGestures(gestureSet).ToArray();
        return gestures.Length > 0 &&
               gestures.All(gesture => TryParse(gesture, out _, out _));
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            Key.OemPlus => "Plus",
            Key.OemMinus => "Minus",
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            Key.Back => "Backspace",
            Key.Space => "Space",
            Key.Escape => "Escape",
            Key.Delete => "Delete",
            _ => key.ToString()
        };
    }

    private static bool TryParseKey(string text, out Key key)
    {
        text = text.Trim();

        var aliases = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["0"] = Key.D0,
            ["1"] = Key.D1,
            ["2"] = Key.D2,
            ["3"] = Key.D3,
            ["4"] = Key.D4,
            ["5"] = Key.D5,
            ["6"] = Key.D6,
            ["7"] = Key.D7,
            ["8"] = Key.D8,
            ["9"] = Key.D9,
            ["plus"] = Key.OemPlus,
            ["minus"] = Key.OemMinus,
            ["comma"] = Key.OemComma,
            ["period"] = Key.OemPeriod,
            ["backspace"] = Key.Back,
            ["space"] = Key.Space,
            ["esc"] = Key.Escape,
            ["escape"] = Key.Escape,
            ["del"] = Key.Delete,
            ["delete"] = Key.Delete,
            ["return"] = Key.Enter,
            ["enter"] = Key.Enter
        };

        if (aliases.TryGetValue(text, out key))
        {
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && key != Key.None;
    }
}
