using System.Windows;

namespace WcImageViewer.Services;

public enum AppTheme
{
    Dark,
    Light
}

public static class ThemeService
{
    private static readonly Uri LightThemeUri = new("Styles/LightTheme.xaml", UriKind.Relative);

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static event EventHandler? ThemeChanged;

    public static AppTheme Parse(string? value)
    {
        return string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Light
            : AppTheme.Dark;
    }

    public static string ToSetting(AppTheme theme)
    {
        return theme == AppTheme.Light ? "Light" : "Dark";
    }

    public static void Apply(AppTheme theme)
    {
        var application = Application.Current;
        if (application is not null)
        {
            var dictionaries = application.Resources.MergedDictionaries;
            ResourceDictionary? lightTheme = null;
            foreach (var dictionary in dictionaries)
            {
                if (string.Equals(
                        dictionary.Source?.OriginalString,
                        LightThemeUri.OriginalString,
                        StringComparison.OrdinalIgnoreCase))
                {
                    lightTheme = dictionary;
                    break;
                }
            }

            if (theme == AppTheme.Light)
            {
                if (lightTheme is null)
                {
                    dictionaries.Add(new ResourceDictionary { Source = LightThemeUri });
                }
            }
            else if (lightTheme is not null)
            {
                dictionaries.Remove(lightTheme);
            }
        }

        if (Current == theme)
        {
            return;
        }

        Current = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
