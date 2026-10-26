using System.IO;
using System.IO.Compression;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class UpdateInstallerTests
{
    [Fact]
    public void PreparePackageFindsFilesInNestedFolder()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var zip = CreateZip(
                root,
                ("WcImageViewer-0.4.1/WcImageViewer.exe", new byte[] { 1, 2, 3 }),
                ("WcImageViewer-0.4.1/WcImageViewer.Updater.exe", new byte[] { 4, 5, 6 }),
                ("WcImageViewer-0.4.1/LICENSE", new byte[] { 7 }),
                ("WcImageViewer-0.4.1/THIRD-PARTY-NOTICES.md", new byte[] { 8 }));

            var package = UpdateInstaller.PreparePackage(
                zip,
                new UpdateRelease { Tag = "v0.4.1" },
                root);

            Assert.Equal(UpdateInstaller.AppFileName, Path.GetFileName(package.AppPath));
            Assert.Equal(UpdateInstaller.UpdaterFileName, Path.GetFileName(package.UpdaterPath));
            Assert.Contains("WcImageViewer-0.4.1", package.AppPath);
            Assert.True(File.Exists(package.AppPath));
            Assert.True(File.Exists(package.UpdaterPath));
            Assert.True(File.Exists(Path.Combine(package.StagingDirectory, "WcImageViewer-0.4.1", "LICENSE")));
            Assert.StartsWith(Path.Combine(root, "update-v0.4.1-"), package.StagingDirectory);
            Assert.StartsWith(Path.Combine(root, "logs"), package.LogPath);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void PreparePackageWithoutUpdaterIsAllowed()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var zip = CreateZip(
                root,
                ("WcImageViewer.exe", new byte[] { 1 }),
                ("LICENSE", new byte[] { 7 }));

            var package = UpdateInstaller.PreparePackage(
                zip,
                new UpdateRelease { Tag = "v0.4.1" },
                root);

            Assert.True(File.Exists(package.AppPath));
            Assert.Null(package.UpdaterPath);
            Assert.True(Directory.Exists(package.StagingDirectory));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void PreparePackageWithoutAppFails()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var zip = CreateZip(root, ("WcImageViewer.Updater.exe", new byte[] { 1 }));

            var error = Assert.Throws<InvalidOperationException>(
                () => UpdateInstaller.PreparePackage(zip, new UpdateRelease { Tag = "v0.4.1" }, root));

            Assert.Contains(UpdateInstaller.AppFileName, error.Message);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void ResolveUpdaterPrefersInstalledUpdater()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);
            var newUpdater = Path.Combine(staging, UpdateInstaller.UpdaterFileName);
            File.WriteAllText(newUpdater, "new");

            var targetDirectory = Path.Combine(root, "target");
            Directory.CreateDirectory(targetDirectory);
            var targetUpdater = Path.Combine(targetDirectory, UpdateInstaller.UpdaterFileName);
            File.WriteAllText(targetUpdater, "current");

            var package = new UpdatePackage
            {
                UpdaterPath = newUpdater,
                StagingDirectory = staging
            };

            var selected = UpdateInstaller.ResolveUpdater(package, targetUpdater, () => null);

            Assert.Equal(Path.Combine(staging, "run", UpdateInstaller.UpdaterFileName), selected);
            Assert.Equal("current", File.ReadAllText(selected));
            Assert.Equal("new", File.ReadAllText(newUpdater));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void ResolveUpdaterPrefersEmbeddedUpdater()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);
            var zipUpdater = Path.Combine(staging, UpdateInstaller.UpdaterFileName);
            File.WriteAllText(zipUpdater, "zip");

            var targetDirectory = Path.Combine(root, "target");
            Directory.CreateDirectory(targetDirectory);
            var targetUpdater = Path.Combine(targetDirectory, UpdateInstaller.UpdaterFileName);
            File.WriteAllText(targetUpdater, "current");

            var embeddedDirectory = Path.Combine(root, "embedded");
            Directory.CreateDirectory(embeddedDirectory);
            var embeddedUpdater = Path.Combine(embeddedDirectory, UpdateInstaller.UpdaterFileName);
            File.WriteAllText(embeddedUpdater, "embedded");

            var package = new UpdatePackage
            {
                UpdaterPath = zipUpdater,
                StagingDirectory = staging
            };

            var selected = UpdateInstaller.ResolveUpdater(package, targetUpdater, () => embeddedUpdater);

            Assert.Equal(embeddedUpdater, selected);
            Assert.False(Directory.Exists(Path.Combine(staging, "run")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void ResolveUpdaterFailsWhenNoUpdaterExistsAnywhere()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);
            var package = new UpdatePackage
            {
                UpdaterPath = null,
                StagingDirectory = staging
            };

            var error = Assert.Throws<InvalidOperationException>(
                () => UpdateInstaller.ResolveUpdater(
                    package,
                    Path.Combine(root, "missing", UpdateInstaller.UpdaterFileName),
                    () => null));

            Assert.Contains(UpdateInstaller.UpdaterFileName, error.Message);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void ResolveUpdaterFallsBackToPackageUpdater()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);
            var newUpdater = Path.Combine(staging, UpdateInstaller.UpdaterFileName);
            File.WriteAllText(newUpdater, "new");

            var package = new UpdatePackage
            {
                UpdaterPath = newUpdater,
                StagingDirectory = staging
            };

            var selected = UpdateInstaller.ResolveUpdater(
                package,
                Path.Combine(root, "missing", UpdateInstaller.UpdaterFileName),
                () => null);

            Assert.Equal(newUpdater, selected);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void CleanupLegacyUpdaterDeletesUpdaterNextToApp()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var appPath = Path.Combine(root, UpdateInstaller.AppFileName);
            File.WriteAllText(appPath, "app");
            var updaterPath = Path.Combine(root, UpdateInstaller.UpdaterFileName);
            File.WriteAllText(updaterPath, "legacy");

            var removed = UpdateInstaller.CleanupLegacyUpdater(appPath, attempts: 1, delayMilliseconds: 0);

            Assert.True(removed);
            Assert.False(File.Exists(updaterPath));
            Assert.True(File.Exists(appPath));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void CleanupLegacyUpdaterIgnoresLockedUpdater()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var appPath = Path.Combine(root, UpdateInstaller.AppFileName);
            File.WriteAllText(appPath, "app");
            var updaterPath = Path.Combine(root, UpdateInstaller.UpdaterFileName);
            File.WriteAllText(updaterPath, "legacy");

            using (new FileStream(updaterPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var removed = UpdateInstaller.CleanupLegacyUpdater(
                    appPath,
                    attempts: 2,
                    delayMilliseconds: 0);

                Assert.False(removed);
                Assert.True(File.Exists(updaterPath));
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void CleanupLegacyUpdaterSucceedsWhenUpdaterIsAlreadyGone()
    {
        var root = TestTemp.CreateFolder("wciv_installer_tests");
        try
        {
            var appPath = Path.Combine(root, UpdateInstaller.AppFileName);
            File.WriteAllText(appPath, "app");

            Assert.True(UpdateInstaller.CleanupLegacyUpdater(appPath, attempts: 1, delayMilliseconds: 0));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void BuildArgumentsKeepsPathsWithSpacesKoreanAndQuoteAsSingleArguments()
    {
        var targetApp = @"C:\Program Files\WC Image Viewer\WcImageViewer.exe";
        var targetUpdater = @"C:\Program Files\WC Image Viewer\WcImageViewer.Updater.exe";
        var package = new UpdatePackage
        {
            AppPath = @"C:\Temp\내 폴더\O'Brien\WcImageViewer.exe",
            UpdaterPath = @"C:\Temp\내 폴더\O'Brien\WcImageViewer.Updater.exe",
            StagingDirectory = @"C:\Temp\내 폴더\O'Brien\staging",
            LogPath = @"C:\Temp\내 폴더\로그 파일.log"
        };

        var arguments = UpdateInstaller.BuildArguments(package, targetApp, targetUpdater, 4242);

        Assert.Equal(14, arguments.Count);
        Assert.Equal("--parent-pid", arguments[0]);
        Assert.Equal("4242", arguments[1]);

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i += 2)
        {
            pairs[arguments[i]] = arguments[i + 1];
        }

        Assert.Equal(targetApp, pairs["--target-app"]);
        Assert.Equal(package.AppPath, pairs["--new-app"]);
        Assert.Equal(targetUpdater, pairs["--target-updater"]);
        Assert.Equal(package.UpdaterPath, pairs["--new-updater"]);
        Assert.Equal(package.StagingDirectory, pairs["--staging"]);
        Assert.Equal(package.LogPath, pairs["--log"]);
        Assert.All(arguments, argument => Assert.DoesNotContain('"', argument));
    }

    [Fact]
    public void BuildArgumentsSkipsUpdaterCopyWhenPackageHasNoUpdater()
    {
        var targetApp = @"C:\Apps\WcImageViewer.exe";
        var targetUpdater = @"C:\Apps\WcImageViewer.Updater.exe";
        var package = new UpdatePackage
        {
            AppPath = @"C:\Temp\WcImageViewer.exe",
            UpdaterPath = null,
            StagingDirectory = @"C:\Temp\staging",
            LogPath = @"C:\Temp\update.log"
        };

        var arguments = UpdateInstaller.BuildArguments(package, targetApp, targetUpdater, 7);

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i += 2)
        {
            pairs[arguments[i]] = arguments[i + 1];
        }

        Assert.Equal(targetUpdater, pairs["--new-updater"]);
        Assert.Equal(targetUpdater, pairs["--target-updater"]);
    }

    private static string CreateZip(
        string root,
        params (string Path, byte[] Content)[] entries)
    {
        var zipPath = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath);
            using var stream = entry.Open();
            stream.Write(content);
        }

        return zipPath;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
