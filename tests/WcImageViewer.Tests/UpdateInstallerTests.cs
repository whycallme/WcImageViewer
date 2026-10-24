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
        var root = CreateTempRoot();
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
    public void PreparePackageWithoutUpdaterFailsAndCleansStaging()
    {
        var root = CreateTempRoot();
        try
        {
            var zip = CreateZip(root, ("WcImageViewer.exe", new byte[] { 1 }));

            var error = Assert.Throws<InvalidOperationException>(
                () => UpdateInstaller.PreparePackage(zip, new UpdateRelease { Tag = "v0.4.1" }, root));

            Assert.Contains(UpdateInstaller.UpdaterFileName, error.Message);
            Assert.Empty(Directory.GetDirectories(root, "update-*"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void PreparePackageWithoutAppFails()
    {
        var root = CreateTempRoot();
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
        var root = CreateTempRoot();
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

            var selected = UpdateInstaller.ResolveUpdater(package, targetUpdater);

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
    public void ResolveUpdaterFallsBackToPackageUpdater()
    {
        var root = CreateTempRoot();
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
                Path.Combine(root, "missing", UpdateInstaller.UpdaterFileName));

            Assert.Equal(newUpdater, selected);
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

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "wciv-installer-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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
