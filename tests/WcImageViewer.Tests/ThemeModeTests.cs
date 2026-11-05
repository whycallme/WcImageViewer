using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class ThemeModeTests
{
    [Theory]
    [InlineData("Light", AppThemeMode.Light)]
    [InlineData("light", AppThemeMode.Light)]
    [InlineData("Dark", AppThemeMode.Dark)]
    [InlineData("dark", AppThemeMode.Dark)]
    [InlineData("System", AppThemeMode.System)]
    [InlineData("", AppThemeMode.System)]
    [InlineData("Blue", AppThemeMode.System)]
    [InlineData(null, AppThemeMode.System)]
    public void ParseModeReadsLegacyValuesAndDefaultsToSystem(string? value, AppThemeMode expected)
    {
        Assert.Equal(expected, ThemeService.ParseMode(value));
    }

    [Theory]
    [InlineData(AppThemeMode.Light, "Light")]
    [InlineData(AppThemeMode.Dark, "Dark")]
    [InlineData(AppThemeMode.System, "System")]
    public void ToSettingWritesModeName(AppThemeMode mode, string expected)
    {
        Assert.Equal(expected, ThemeService.ToSetting(mode));
        Assert.Equal(mode, ThemeService.ParseMode(ThemeService.ToSetting(mode)));
    }

    [Theory]
    [InlineData(AppThemeMode.Light, true, AppTheme.Light)]
    [InlineData(AppThemeMode.Light, false, AppTheme.Light)]
    [InlineData(AppThemeMode.Dark, true, AppTheme.Dark)]
    [InlineData(AppThemeMode.Dark, false, AppTheme.Dark)]
    [InlineData(AppThemeMode.System, true, AppTheme.Light)]
    [InlineData(AppThemeMode.System, false, AppTheme.Dark)]
    public void ResolveThemeFollowsOsOnlyInSystemMode(AppThemeMode mode, bool systemPrefersLight, AppTheme expected)
    {
        Assert.Equal(expected, ThemeService.ResolveTheme(mode, systemPrefersLight));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(null, true)]
    [InlineData("0", true)]
    public void RegistryValueMapsToLightFlag(object? value, bool expected)
    {
        Assert.Equal(expected, ThemeService.IsLightFromRegistryValue(value));
    }
}
