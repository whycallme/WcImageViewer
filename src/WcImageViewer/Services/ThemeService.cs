using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace WcImageViewer.Services;

public enum AppTheme
{
    Dark,
    Light
}

/// <summary>사용자가 설정에서 고르는 테마 모드. System 이면 OS 앱 테마를 따른다.</summary>
public enum AppThemeMode
{
    System,
    Light,
    Dark
}

public static class ThemeService
{
    private const string PersonalizeKeyPath =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private static readonly Uri LightThemeUri = new("Styles/LightTheme.xaml", UriKind.Relative);
    private static bool _watchingSystem;

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static AppThemeMode Mode { get; private set; } = AppThemeMode.System;

    public static event EventHandler? ThemeChanged;

    public static AppThemeMode ParseMode(string? value)
    {
        if (string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase))
        {
            return AppThemeMode.Light;
        }

        if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return AppThemeMode.Dark;
        }

        // 값이 없거나 알 수 없으면 시스템.
        return AppThemeMode.System;
    }

    public static string ToSetting(AppThemeMode mode)
    {
        return mode switch
        {
            AppThemeMode.Light => "Light",
            AppThemeMode.Dark => "Dark",
            _ => "System"
        };
    }

    /// <summary>모드와 OS 테마 여부로 실제 적용할 테마를 정한다 (순수 함수).</summary>
    public static AppTheme ResolveTheme(AppThemeMode mode, bool systemPrefersLight)
    {
        return mode switch
        {
            AppThemeMode.Light => AppTheme.Light,
            AppThemeMode.Dark => AppTheme.Dark,
            _ => systemPrefersLight ? AppTheme.Light : AppTheme.Dark
        };
    }

    /// <summary>AppsUseLightTheme 레지스트리 값을 해석한다. 값이 없으면 Windows 기본(라이트)으로 본다.</summary>
    public static bool IsLightFromRegistryValue(object? value)
    {
        return value is int number ? number != 0 : true;
    }

    public static bool ReadSystemPrefersLight()
    {
        try
        {
            return IsLightFromRegistryValue(Registry.GetValue(PersonalizeKeyPath, AppsUseLightThemeValue, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException or ArgumentException)
        {
            return true;
        }
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

    /// <summary>모드를 저장하고 실제 테마를 적용한다.</summary>
    public static void ApplyMode(AppThemeMode mode)
    {
        Mode = mode;
        Apply(ResolveTheme(mode, ReadSystemPrefersLight()));
    }

    /// <summary>실행 중 OS 테마 변경을 감지해 시스템 모드일 때 자동 전환한다.</summary>
    public static void StartSystemWatch()
    {
        if (_watchingSystem)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _watchingSystem = true;
    }

    public static void StopSystemWatch()
    {
        if (!_watchingSystem)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watchingSystem = false;
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        // 이 이벤트는 UI 스레드가 아닌 곳에서 올라온다.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            if (Mode == AppThemeMode.System)
            {
                ApplyMode(AppThemeMode.System);
            }
        }));
    }
}
