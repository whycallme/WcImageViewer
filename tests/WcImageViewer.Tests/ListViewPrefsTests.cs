using System.IO;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class ListViewPrefsTests
{
    [Fact]
    public void GlobalScopeReturnsGlobalCopy()
    {
        var settings = new AppSettings();
        settings.ListViewGlobal = new ListViewPrefs
        {
            ThumbSize = 200,
            SortKey = ListSortKey.Size,
            SortDescending = true
        };

        var result = ListViewPrefsResolver.Get(settings, @"C:\Some\Folder");

        Assert.Equal(200, result.ThumbSize);
        Assert.Equal(ListSortKey.Size, result.SortKey);
        Assert.True(result.SortDescending);

        result.ThumbSize = 999;
        Assert.Equal(200, settings.ListViewGlobal.ThumbSize);
    }

    [Fact]
    public void PerFolderScopeReturnsFolderPrefsWhenPresent()
    {
        var settings = new AppSettings();
        settings.ListViewScope = ListPrefsScope.PerFolder;

        var prefs = new ListViewPrefs { ThumbSize = 123, SortKey = ListSortKey.Extension };
        ListViewPrefsResolver.Set(settings, @"C:\Foo\", prefs);

        var result = ListViewPrefsResolver.Get(settings, @"C:\Foo");
        Assert.Equal(123, result.ThumbSize);
        Assert.Equal(ListSortKey.Extension, result.SortKey);
    }

    [Fact]
    public void PerFolderScopeFallsBackToGlobalCopyWhenMissing()
    {
        var settings = new AppSettings();
        settings.ListViewScope = ListPrefsScope.PerFolder;
        settings.ListViewGlobal = new ListViewPrefs { ThumbSize = 180 };

        var result = ListViewPrefsResolver.Get(settings, @"C:\Missing");

        Assert.Equal(180, result.ThumbSize);
        result.ThumbSize = 1;
        Assert.Equal(180, settings.ListViewGlobal.ThumbSize);
    }

    [Theory]
    [InlineData(10, 64)]
    [InlineData(400, 320)]
    [InlineData(160, 160)]
    public void SetClampsThumbSize(int input, int expected)
    {
        var settings = new AppSettings();
        ListViewPrefsResolver.Set(settings, @"C:\Foo", new ListViewPrefs { ThumbSize = input });
        Assert.Equal(expected, settings.ListViewGlobal.ThumbSize);
    }

    [Fact]
    public void SetNormalizesPathCaseAndTrailingSeparator()
    {
        var settings = new AppSettings();
        settings.ListViewScope = ListPrefsScope.PerFolder;

        ListViewPrefsResolver.Set(settings, @"C:\Foo\", new ListViewPrefs { ThumbSize = 210 });

        var result = ListViewPrefsResolver.Get(settings, @"c:\foo");
        Assert.Equal(210, result.ThumbSize);
    }

    [Fact]
    public void SetGlobalScopeStoresInGlobal()
    {
        var settings = new AppSettings();
        settings.ListViewScope = ListPrefsScope.Global;

        ListViewPrefsResolver.Set(settings, @"C:\Foo", new ListViewPrefs { ThumbSize = 250 });

        Assert.Equal(250, settings.ListViewGlobal.ThumbSize);
        Assert.Empty(settings.ListViewFolderPrefs);
    }

    [Fact]
    public void PerFolderMapIsCappedAt500()
    {
        var settings = new AppSettings();
        settings.ListViewScope = ListPrefsScope.PerFolder;

        for (var i = 0; i < 501; i++)
        {
            ListViewPrefsResolver.Set(settings, $@"C:\Folder{i}", new ListViewPrefs { ThumbSize = 160 });
        }

        Assert.True(settings.ListViewFolderPrefs.Count <= 500);
    }

    [Fact]
    public void JsonRoundTripAndOldSettingsLoad()
    {
        var folder = TestTemp.CreateFolder("wciv_lv");
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Version\":2}");

            var loaded = SettingsService.ForPath(path);
            Assert.Equal(ListPrefsScope.Global, loaded.Settings.ListViewScope);
            Assert.Equal(160, loaded.Settings.ListViewGlobal.ThumbSize);
            Assert.NotNull(loaded.Settings.ListViewFolderPrefs);

            loaded.Settings.ListViewScope = ListPrefsScope.PerFolder;
            loaded.Settings.ListViewGlobal = new ListViewPrefs { ThumbSize = 222, SortKey = ListSortKey.Modified, SortDescending = true };
            ListViewPrefsResolver.Set(loaded.Settings, @"C:\Foo\", new ListViewPrefs { ThumbSize = 300 });
            loaded.Save();

            var reloaded = SettingsService.ForPath(path);
            Assert.Equal(ListPrefsScope.PerFolder, reloaded.Settings.ListViewScope);
            Assert.Equal(222, reloaded.Settings.ListViewGlobal.ThumbSize);
            Assert.Equal(ListSortKey.Modified, reloaded.Settings.ListViewGlobal.SortKey);
            Assert.True(reloaded.Settings.ListViewGlobal.SortDescending);

            var key = Path.GetFullPath(@"C:\Foo").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
            Assert.True(reloaded.Settings.ListViewFolderPrefs.ContainsKey(key));
            Assert.Equal(300, reloaded.Settings.ListViewFolderPrefs[key].ThumbSize);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
