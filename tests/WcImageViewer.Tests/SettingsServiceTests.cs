using System.IO;
using WcImageViewer.Models;
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

    [Fact]
    public void NewUpdateSettingsDefaultToAutomaticChecks()
    {
        var folder = CreateTempFolder();
        try
        {
            var service = SettingsService.ForPath(Path.Combine(folder, "settings.json"));

            Assert.True(service.Settings.AutoCheckUpdates);
            Assert.Equal(string.Empty, service.Settings.SkippedVersion);
            Assert.Null(service.Settings.LastUpdateCheckUtc);
            Assert.Null(service.Settings.PendingUpdate);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SavesAndLoadsUpdateState()
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var checkedAt = new DateTime(2026, 10, 24, 3, 4, 5, DateTimeKind.Utc);
            var service = SettingsService.ForPath(path);
            service.Settings.AutoCheckUpdates = false;
            service.Settings.SkippedVersion = " v0.5.0 ";
            service.Settings.LastUpdateCheckUtc = checkedAt;
            service.Settings.PendingUpdate = new UpdateRelease
            {
                Tag = "v0.5.0",
                Name = "Release v0.5.0",
                Body = "notes",
                HtmlUrl = "https://example.test/releases/v0.5.0",
                AssetName = UpdateService.PortableAssetName,
                AssetUrl = "https://example.test/WcImageViewer-portable-win-x64.zip",
                AssetSize = 2048,
                AssetDigest = "sha256:0123456789abcdef"
            };
            service.Save();

            var loaded = SettingsService.ForPath(path);

            Assert.False(loaded.Settings.AutoCheckUpdates);
            Assert.Equal("0.5.0", loaded.Settings.SkippedVersion);
            Assert.Equal(checkedAt, loaded.Settings.LastUpdateCheckUtc);
            var pending = loaded.Settings.PendingUpdate;
            Assert.NotNull(pending);
            Assert.Equal("v0.5.0", pending.Tag);
            Assert.Equal("Release v0.5.0", pending.Name);
            Assert.Equal("notes", pending.Body);
            Assert.Equal("https://example.test/releases/v0.5.0", pending.HtmlUrl);
            Assert.Equal(UpdateService.PortableAssetName, pending.AssetName);
            Assert.Equal(2048, pending.AssetSize);
            Assert.Equal("sha256:0123456789abcdef", pending.AssetDigest);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("v0.3.0")]
    [InlineData("v0.1.0")]
    [InlineData("not-a-version")]
    public void NormalizeDropsPendingThatIsNotNewer(string tag)
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var service = SettingsService.ForPath(path);
            service.Settings.PendingUpdate = new UpdateRelease
            {
                Tag = tag,
                AssetUrl = "https://example.test/WcImageViewer-portable-win-x64.zip",
                AssetDigest = "sha256:0123456789abcdef"
            };
            service.Save();

            var loaded = SettingsService.ForPath(path);

            Assert.Null(loaded.Settings.PendingUpdate);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NormalizeKeepsPendingNewerThanCurrentVersion()
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var service = SettingsService.ForPath(path);
            service.Settings.PendingUpdate = new UpdateRelease
            {
                Tag = "v99.0.0",
                AssetUrl = "https://example.test/WcImageViewer-portable-win-x64.zip",
                AssetDigest = "sha256:0123456789abcdef"
            };
            service.Save();

            var loaded = SettingsService.ForPath(path);

            Assert.Equal("v99.0.0", loaded.Settings.PendingUpdate?.Tag);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SkippingVersionKeepsPendingForReask()
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var service = SettingsService.ForPath(path);
            service.Settings.SkippedVersion = "0.5.0";
            service.Settings.PendingUpdate = new UpdateRelease
            {
                Tag = "v0.5.0",
                AssetUrl = "https://example.test/WcImageViewer-portable-win-x64.zip",
                AssetDigest = "sha256:0123456789abcdef"
            };
            service.Save();

            var skipped = SettingsService.ForPath(path);
            Assert.Equal("0.5.0", skipped.Settings.SkippedVersion);
            Assert.Equal("v0.5.0", skipped.Settings.PendingUpdate?.Tag);

            skipped.Settings.SkippedVersion = string.Empty;
            skipped.Save();

            var reask = SettingsService.ForPath(path);
            Assert.Equal(string.Empty, reask.Settings.SkippedVersion);
            Assert.Equal("v0.5.0", reask.Settings.PendingUpdate?.Tag);
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
