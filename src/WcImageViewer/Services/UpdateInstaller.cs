using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

public static class UpdateInstaller
{
    public const string AppFileName = "WcImageViewer.exe";
    public const string UpdaterFileName = "WcImageViewer.Updater.exe";
    public const string EmbeddedUpdaterResourceName = "WcImageViewer.Updater.exe";

    public static string WorkRoot => Path.Combine(Path.GetTempPath(), "WcImageViewer");

    public static bool HasEmbeddedUpdater =>
        typeof(UpdateInstaller).Assembly.GetManifestResourceInfo(EmbeddedUpdaterResourceName) is not null;

    public static UpdatePackage PreparePackage(
        string zipPath,
        UpdateRelease release,
        string? workRoot = null)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("업데이트 패키지를 찾을 수 없습니다.", zipPath);
        }

        var root = workRoot ?? WorkRoot;
        var staging = Path.Combine(root, CreateDirectoryName(release));
        Directory.CreateDirectory(staging);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);

            var appPath = FindFile(staging, AppFileName)
                ?? throw new InvalidOperationException($"업데이트 패키지에 {AppFileName}가 없습니다.");

            // An updater in the ZIP is optional since v0.4.1: the app carries its own.
            var updaterPath = FindFile(staging, UpdaterFileName);

            return new UpdatePackage
            {
                AppPath = appPath,
                UpdaterPath = updaterPath,
                StagingDirectory = staging,
                LogPath = Path.Combine(root, "logs", CreateLogName(release))
            };
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    public static string? ExtractEmbeddedUpdater(string? workRoot = null)
    {
        try
        {
            using var resource = typeof(UpdateInstaller).Assembly
                .GetManifestResourceStream(EmbeddedUpdaterResourceName);
            if (resource is null)
            {
                return null;
            }

            var directory = Path.Combine(
                workRoot ?? WorkRoot,
                "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, UpdaterFileName);
            using var file = File.Create(path);
            resource.CopyTo(file);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string ResolveUpdater(
        UpdatePackage package,
        string targetUpdaterPath,
        Func<string?>? embeddedUpdaterProvider = null)
    {
        var embedded = (embeddedUpdaterProvider ?? (() => ExtractEmbeddedUpdater()))();
        if (!string.IsNullOrEmpty(embedded) && File.Exists(embedded))
        {
            // The updater embedded in the installed app is always preferred: it is
            // the version that matches this build and leaves no file next to the app.
            return embedded;
        }

        if (File.Exists(targetUpdaterPath))
        {
            // Run the updater that belongs to the installed app, not the new one,
            // so a broken new updater cannot break the update itself.
            var runDirectory = Path.Combine(package.StagingDirectory, "run");
            Directory.CreateDirectory(runDirectory);
            var stagedUpdater = Path.Combine(runDirectory, UpdaterFileName);
            File.Copy(targetUpdaterPath, stagedUpdater, overwrite: true);
            return stagedUpdater;
        }

        if (package.UpdaterPath is { } packageUpdater && File.Exists(packageUpdater))
        {
            return packageUpdater;
        }

        throw new InvalidOperationException(
            $"업데이트 프로그램({UpdaterFileName})을 찾을 수 없습니다. " +
            "앱을 다시 설치하거나 릴리즈 ZIP을 직접 내려받아 주세요.");
    }

    public static IReadOnlyList<string> BuildArguments(
        UpdatePackage package,
        string targetAppPath,
        string targetUpdaterPath,
        int parentProcessId)
    {
        return new[]
        {
            "--parent-pid", parentProcessId.ToString(CultureInfo.InvariantCulture),
            "--target-app", targetAppPath,
            "--new-app", package.AppPath,
            "--target-updater", targetUpdaterPath,
            // When the package has no updater, point at the target path so the
            // updater skips copying itself and the folder stays single-exe.
            "--new-updater", package.UpdaterPath ?? targetUpdaterPath,
            "--staging", package.StagingDirectory,
            "--log", package.LogPath
        };
    }

    public static Process Launch(
        UpdatePackage package,
        string? targetAppPath = null,
        int? parentProcessId = null)
    {
        var targetApp = targetAppPath
            ?? Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(targetApp) || !File.Exists(targetApp))
        {
            throw new InvalidOperationException("현재 실행 파일 경로를 찾을 수 없습니다.");
        }

        var targetDirectory = Path.GetDirectoryName(targetApp)
            ?? throw new InvalidOperationException("현재 실행 파일 폴더를 찾을 수 없습니다.");
        var targetUpdater = Path.Combine(targetDirectory, UpdaterFileName);
        var updater = ResolveUpdater(package, targetUpdater);

        var startInfo = new ProcessStartInfo(updater)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = package.StagingDirectory
        };

        foreach (var argument in BuildArguments(
                     package,
                     targetApp,
                     targetUpdater,
                     parentProcessId ?? Environment.ProcessId))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("업데이트 프로그램을 시작할 수 없습니다.");

        if (process.WaitForExit(1000))
        {
            throw new InvalidOperationException(
                $"업데이트 프로그램이 즉시 종료되었습니다 (종료 코드 {process.ExitCode}).");
        }

        return process;
    }

    public static void CleanupStale()
    {
        try
        {
            if (!Directory.Exists(WorkRoot))
            {
                return;
            }

            foreach (var pattern in new[] { "update-*", "run-*" })
            {
                foreach (var directory in Directory.EnumerateDirectories(WorkRoot, pattern))
                {
                    TryDeleteDirectory(directory);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void CleanupLegacyUpdater()
    {
        if (!HasEmbeddedUpdater)
        {
            return;
        }

        var appPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(appPath))
        {
            CleanupLegacyUpdater(appPath, attempts: 20, delayMilliseconds: 1000);
        }
    }

    public static bool CleanupLegacyUpdater(
        string appPath,
        int attempts = 20,
        int delayMilliseconds = 1000)
    {
        var directory = Path.GetDirectoryName(appPath);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        var legacyUpdater = Path.Combine(directory, UpdaterFileName);
        var maxAttempts = Math.Max(1, attempts);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (!File.Exists(legacyUpdater))
            {
                return true;
            }

            try
            {
                File.Delete(legacyUpdater);
                return true;
            }
            catch (IOException)
            {
                // Still running from the previous update; retry briefly.
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (attempt + 1 < maxAttempts && delayMilliseconds > 0)
            {
                Thread.Sleep(delayMilliseconds);
            }
        }

        return !File.Exists(legacyUpdater);
    }

    private static string? FindFile(string root, string fileName)
    {
        return Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();
    }

    private static string CreateDirectoryName(UpdateRelease release)
    {
        var tag = string.IsNullOrWhiteSpace(release.Tag) ? "latest" : release.Tag.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            tag = tag.Replace(invalid, '_');
        }

        return $"update-{tag}-{Guid.NewGuid():N}";
    }

    private static string CreateLogName(UpdateRelease release)
    {
        var tag = string.IsNullOrWhiteSpace(release.Tag) ? "latest" : release.Tag.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            tag = tag.Replace(invalid, '_');
        }

        return $"updater-{tag}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
