using System.IO;
using WcImageViewer;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class ListViewLayoutTests
{
    [Fact]
    public void ListViewLayout_DefaultValues()
    {
        var layout = new ListViewLayout();

        Assert.Null(layout.WindowLeft);
        Assert.Null(layout.WindowTop);
        Assert.Null(layout.WindowWidth);
        Assert.Null(layout.WindowHeight);
        Assert.False(layout.Maximized);
        Assert.Equal(260, layout.LeftColumnWidth);
        Assert.Equal(0.33, layout.LeftTopHeightRatio);
        Assert.Equal(160, layout.InfoHeight);
    }

    [Fact]
    public void Normalize_ClampsLeftTopHeightRatio()
    {
        var folder = TestTemp.CreateFolder("wciv_lvlayout");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Version\":2,\"ListViewLayout\":{\"LeftTopHeightRatio\":5}}");
            var loaded = SettingsService.ForPath(path);
            Assert.Equal(0.9, loaded.Settings.ListViewLayout.LeftTopHeightRatio);

            File.WriteAllText(path, "{\"Version\":2,\"ListViewLayout\":{\"LeftTopHeightRatio\":-1}}");
            var loaded2 = SettingsService.ForPath(path);
            Assert.Equal(0.1, loaded2.Settings.ListViewLayout.LeftTopHeightRatio);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Normalize_ClampsInfoHeight()
    {
        var folder = TestTemp.CreateFolder("wciv_lvlayout");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Version\":2,\"ListViewLayout\":{\"InfoHeight\":10}}");
            var loaded = SettingsService.ForPath(path);
            Assert.Equal(80, loaded.Settings.ListViewLayout.InfoHeight);

            File.WriteAllText(path, "{\"Version\":2,\"ListViewLayout\":{\"InfoHeight\":9999}}");
            var loaded2 = SettingsService.ForPath(path);
            Assert.Equal(500, loaded2.Settings.ListViewLayout.InfoHeight);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Normalize_ClampsLeftColumnWidth()
    {
        var folder = TestTemp.CreateFolder("wciv_lvlayout");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Version\":2,\"ListViewLayout\":{\"LeftColumnWidth\":0}}");
            var loaded = SettingsService.ForPath(path);
            Assert.True(loaded.Settings.ListViewLayout.LeftColumnWidth >= 180);

            File.WriteAllText(path, "{\"Version\":2,\"ListViewLayout\":{\"LeftColumnWidth\":-5}}");
            var loaded2 = SettingsService.ForPath(path);
            Assert.True(loaded2.Settings.ListViewLayout.LeftColumnWidth >= 180);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Normalize_NullListViewLayout_CreatesNew()
    {
        var folder = TestTemp.CreateFolder("wciv_lvlayout");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Version\":2,\"ListViewLayout\":null}");
            var loaded = SettingsService.ForPath(path);
            Assert.NotNull(loaded.Settings.ListViewLayout);
            Assert.Equal(260, loaded.Settings.ListViewLayout.LeftColumnWidth);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void OldSettingsWithoutListViewLayout_LoadsDefaults()
    {
        var folder = TestTemp.CreateFolder("wciv_lvlayout");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Version\":2}");
            var loaded = SettingsService.ForPath(path);
            Assert.NotNull(loaded.Settings.ListViewLayout);
            Assert.Equal(260, loaded.Settings.ListViewLayout.LeftColumnWidth);
            Assert.Equal(0.33, loaded.Settings.ListViewLayout.LeftTopHeightRatio);
            Assert.Equal(160, loaded.Settings.ListViewLayout.InfoHeight);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void JsonRoundTrip_ValuesEqual()
    {
        var folder = TestTemp.CreateFolder("wciv_lvlayout");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var svc = SettingsService.ForPath(path);
            svc.Settings.ListViewLayout = new ListViewLayout
            {
                WindowLeft = 10.5,
                WindowTop = 20.25,
                WindowWidth = 300.5,
                WindowHeight = 400.75,
                Maximized = true,
                LeftColumnWidth = 200,
                LeftTopHeightRatio = 0.5,
                InfoHeight = 200
            };
            svc.Save();

            var reloaded = SettingsService.ForPath(path);
            var l = reloaded.Settings.ListViewLayout;
            Assert.Equal(10.5, l.WindowLeft);
            Assert.Equal(20.25, l.WindowTop);
            Assert.Equal(300.5, l.WindowWidth);
            Assert.Equal(400.75, l.WindowHeight);
            Assert.True(l.Maximized);
            Assert.Equal(200, l.LeftColumnWidth);
            Assert.Equal(0.5, l.LeftTopHeightRatio);
            Assert.Equal(200, l.InfoHeight);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(100, 100, 1, 256)]
    [InlineData(3000, 2000, 1, 3072)]
    [InlineData(10000, 10000, 2, 4096)]
    [InlineData(500, 300, 2, 1024)]
    public void ComputePreviewDecodeWidth_Cases(double w, double h, double dpiScale, int expected)
    {
        Assert.Equal(expected, ListViewWindow.ComputePreviewDecodeWidth(w, h, dpiScale));
        Assert.Equal(expected, ListViewLayout.ComputePreviewDecodeWidth(w, h, dpiScale));
    }

    [Fact]
    public void ComputePreviewDecodeWidth_AlwaysMultipleOf256()
    {
        for (var i = 0; i < 20; i++)
        {
            var w = i * 137 + 10;
            var h = i * 97 + 5;
            var dpi = (i % 3) + 1;
            var r = ListViewWindow.ComputePreviewDecodeWidth(w, h, dpi);
            Assert.Equal(0, r % 256);
            Assert.InRange(r, 256, 4096);
        }
    }
}
