using System.IO;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void SavesAndLoadsSettings()
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var service = SettingsService.ForPath(path);
            service.Settings.WindowWidth = 1111;
            service.Settings.SlideshowIntervalSeconds = 7;
            service.Settings.SlideshowRepeat = false;
            service.Settings.SlideshowRandom = true;
            service.Settings.ThumbnailsVisible = false;
            service.Settings.DefaultZoomPercent = 120;
            service.Settings.AutoFitWindowToImage = false;
            service.Settings.DoNotUpscaleSmallImages = false;
            service.Settings.ConfirmDelete = false;
            service.Settings.UseRecycleBin = false;
            service.Settings.WrapNavigation = false;
            service.Settings.ShowNavigationNotice = false;
            service.Settings.Theme = "Light";
            service.Settings.EditorPath = @"C:\Tools\editor.exe";
            service.Settings.AssociatedExtensions = new List<string> { ".jpg", ".png" };
            service.Settings.SingleWindowMode = true;
            service.Settings.Shortcuts["Open"] = "F3";
            service.Settings.Bookmarks.Add(folder);
            service.Settings.RecentPaths.Add(folder);
            service.Save();

            var loaded = SettingsService.ForPath(path);

            Assert.Equal(1111, loaded.Settings.WindowWidth);
            Assert.Equal(7, loaded.Settings.SlideshowIntervalSeconds);
            Assert.False(loaded.Settings.SlideshowRepeat);
            Assert.True(loaded.Settings.SlideshowRandom);
            Assert.False(loaded.Settings.ThumbnailsVisible);
            Assert.Equal(120, loaded.Settings.DefaultZoomPercent);
            Assert.False(loaded.Settings.FitWindowToImage);
            Assert.False(loaded.Settings.AutoFitWindowToImage);
            Assert.False(loaded.Settings.DoNotUpscaleSmallImages);
            Assert.False(loaded.Settings.ConfirmDelete);
            Assert.False(loaded.Settings.UseRecycleBin);
            Assert.False(loaded.Settings.WrapNavigation);
            Assert.False(loaded.Settings.ShowNavigationNotice);
            Assert.Equal("Light", loaded.Settings.Theme);
            Assert.Equal(@"C:\Tools\editor.exe", loaded.Settings.EditorPath);
            Assert.Equal(new[] { ".jpg", ".png" }, loaded.Settings.AssociatedExtensions);
            Assert.False(loaded.Settings.AssociateFileTypes);
            Assert.True(loaded.Settings.SingleWindowMode);
            Assert.Equal("F3", loaded.Settings.Shortcuts["Open"]);
            Assert.Contains(folder, loaded.Settings.Bookmarks);
            Assert.Contains(folder, loaded.Settings.RecentPaths);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NewPlaybackSettingsDefaultToPreviousBehavior()
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var service = SettingsService.ForPath(path);

            Assert.True(service.Settings.WrapNavigation);
            Assert.True(service.Settings.ShowNavigationNotice);
            Assert.True(service.Settings.SlideshowRepeat);
            Assert.False(service.Settings.SlideshowRandom);
            Assert.True(service.Settings.DoNotUpscaleSmallImages);
            Assert.Equal("Dark", service.Settings.Theme);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ClampsIntervalAndIgnoresBrokenJson()
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{ broken");

            var service = SettingsService.ForPath(path);
            Assert.Equal(3, service.Settings.SlideshowIntervalSeconds);

            service.Settings.SlideshowIntervalSeconds = 999;
            service.Save();

            var loaded = SettingsService.ForPath(path);
            Assert.Equal(60, loaded.Settings.SlideshowIntervalSeconds);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MigratesLegacyAutoWindowFitSetting()
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var service = SettingsService.ForPath(path);
            service.Settings.Version = 0;
            service.Settings.FitWindowToImage = true;
            service.Settings.DefaultZoomPercent = 100;
            service.Settings.ThumbnailsVisible = true;
            service.Save();

            var loaded = SettingsService.ForPath(path);

            Assert.False(loaded.Settings.FitWindowToImage);
            Assert.True(loaded.Settings.AutoFitWindowToImage);
            Assert.False(loaded.Settings.ThumbnailsVisible);
            Assert.Equal(0, loaded.Settings.DefaultZoomPercent);
            Assert.Equal(1200, loaded.Settings.WindowWidth);
            Assert.Equal(800, loaded.Settings.WindowHeight);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateTempFolder()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "wciv_settings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
