using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WcImageViewer.Services;

// Windows 11 small DWM corner plus an explicit frame border so the system's
// own highlight does not show against the custom chrome. Maximized windows
// are squared by DWM itself; fullscreen (normal state, screen-sized) needs
// the square preference explicitly.
public static class WindowCornerService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpDoNotRound = 1;
    private const int DwmwcpRoundSmall = 3;
    private const int DarkChromeBorderColor = 0x00312D2D; // COLORREF for #2D2D31
    private const int LightChromeBorderColor = 0x00C5D2D8; // COLORREF for #D8D2C5

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window) => SetPreference(window, DwmwcpRoundSmall);

    public static void SetSquare(Window window) => SetPreference(window, DwmwcpDoNotRound);

    public static void ApplyToAllWindows()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (var window in Application.Current.Windows.OfType<Window>())
        {
            Apply(window);
        }
    }

    private static void SetPreference(Window window, int preference)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var light = ThemeService.Current == AppTheme.Light;
        SetAttribute(handle, DwmwaUseImmersiveDarkMode, light ? 0 : 1);
        SetAttribute(handle, DwmwaBorderColor, light ? LightChromeBorderColor : DarkChromeBorderColor);

        var value = preference;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref value, sizeof(int));
    }

    private static void SetAttribute(IntPtr handle, int attribute, int value)
    {
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }
}
