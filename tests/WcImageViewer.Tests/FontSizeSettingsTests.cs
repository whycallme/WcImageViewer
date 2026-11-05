using System.IO;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class FontSizeSettingsTests
{
    [Fact]
    public void AppSettings_DefaultFontSizes()
    {
        var settings = new AppSettings();

        Assert.Equal(12, settings.UiFontSize);
        Assert.Equal(12, settings.ListViewItemFontSize);
    }

    [Fact]
    public void Normalize_ClampsUiFontSize()
    {
        var folder = TestTemp.CreateFolder("wciv_fontsize");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Version\":2,\"UiFontSize\":0}");
            var loaded = SettingsService.ForPath(path);
            Assert.Equal(8, loaded.Settings.UiFontSize);

            File.WriteAllText(path, "{\"Version\":2,\"UiFontSize\":999}");
            var loaded2 = SettingsService.ForPath(path);
            Assert.Equal(48, loaded2.Settings.UiFontSize);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Normalize_ClampsListViewItemFontSize()
    {
        var folder = TestTemp.CreateFolder("wciv_fontsize");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Version\":2,\"ListViewItemFontSize\":0}");
            var loaded = SettingsService.ForPath(path);
            Assert.Equal(8, loaded.Settings.ListViewItemFontSize);

            File.WriteAllText(path, "{\"Version\":2,\"ListViewItemFontSize\":999}");
            var loaded2 = SettingsService.ForPath(path);
            Assert.Equal(48, loaded2.Settings.ListViewItemFontSize);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SaveThenReload_PreservesFontSizes()
    {
        var folder = TestTemp.CreateFolder("wciv_fontsize");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var service = SettingsService.ForPath(path);
            service.Settings.UiFontSize = 14;
            service.Settings.ListViewItemFontSize = 18;
            service.Save();

            var reloaded = SettingsService.ForPath(path);
            Assert.Equal(14, reloaded.Settings.UiFontSize);
            Assert.Equal(18, reloaded.Settings.ListViewItemFontSize);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
