using System.IO;

namespace WcImageViewer.Services;

public sealed record FileOpResult(
    int Succeeded,
    IReadOnlyList<(string Path, string Error)> Failures,
    IReadOnlyList<string> ResultPaths);

public static class FileOperationService
{
    private const string SameFolderReason = "원본 폴더와 대상 폴더가 같습니다 (건너뜀).";

    public static FileOpResult Copy(
        IReadOnlyList<string> files,
        string destFolder,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(destFolder);

        return RunFileOperation(files, destFolder, progress, ct, (src, destPath, _) =>
        {
            File.Copy(src, destPath, overwrite: false);
            return destPath;
        });
    }

    public static FileOpResult Move(
        IReadOnlyList<string> files,
        string destFolder,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(destFolder);

        var failures = new List<(string Path, string Error)>();
        var results = new List<string>();
        int succeeded = 0;
        int total = files.Count;

        if (!EnsureDestinationFolder(destFolder, files, failures))
        {
            ReportProgress(progress, 0, total);
            return new FileOpResult(succeeded, failures, results);
        }

        progress?.Report(0);
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var src = files[i];
            try
            {
                if (string.IsNullOrWhiteSpace(src))
                {
                    throw new ArgumentException("파일 경로가 비어 있습니다.", nameof(src));
                }

                var fullSrc = Path.GetFullPath(src);
                if (!File.Exists(fullSrc))
                {
                    throw new FileNotFoundException("파일을 찾을 수 없습니다.", fullSrc);
                }

                var destFull = Path.GetFullPath(destFolder);
                var srcDir = Path.GetDirectoryName(fullSrc) ?? string.Empty;
                if (string.Equals(srcDir, destFull, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(SameFolderReason);
                }

                var destPath = GetUniqueDestinationPath(destFull, Path.GetFileName(fullSrc));
                if (IsSameDrive(fullSrc, destFull))
                {
                    File.Move(fullSrc, destPath, overwrite: false);
                }
                else
                {
                    File.Copy(fullSrc, destPath, overwrite: false);
                    File.Delete(fullSrc);
                }

                results.Add(destPath);
                succeeded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add((src, ex.Message));
            }

            ReportProgress(progress, i + 1, total);
        }

        progress?.Report(100);
        return new FileOpResult(succeeded, failures, results);
    }

    public static FileOpResult Delete(IReadOnlyList<string> files, bool useRecycleBin)
    {
        ArgumentNullException.ThrowIfNull(files);

        var failures = new List<(string Path, string Error)>();
        var results = new List<string>();
        int succeeded = 0;

        foreach (var src in files)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(src))
                {
                    throw new ArgumentException("파일 경로가 비어 있습니다.");
                }

                var fullSrc = Path.GetFullPath(src);
                if (!File.Exists(fullSrc))
                {
                    throw new FileNotFoundException("파일을 찾을 수 없습니다.", fullSrc);
                }

                FileDeletionService.Delete(fullSrc, useRecycleBin);
                results.Add(fullSrc);
                succeeded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add((src, ex.Message));
            }
        }

        return new FileOpResult(succeeded, failures, results);
    }

    private static FileOpResult RunFileOperation(
        IReadOnlyList<string> files,
        string destFolder,
        IProgress<int>? progress,
        CancellationToken ct,
        Func<string, string, CancellationToken, string> action)
    {
        var failures = new List<(string Path, string Error)>();
        var results = new List<string>();
        int succeeded = 0;
        int total = files.Count;

        if (!EnsureDestinationFolder(destFolder, files, failures))
        {
            ReportProgress(progress, 0, total);
            return new FileOpResult(succeeded, failures, results);
        }

        progress?.Report(0);
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var src = files[i];
            try
            {
                if (string.IsNullOrWhiteSpace(src))
                {
                    throw new ArgumentException("파일 경로가 비어 있습니다.", nameof(src));
                }

                var fullSrc = Path.GetFullPath(src);
                if (!File.Exists(fullSrc))
                {
                    throw new FileNotFoundException("파일을 찾을 수 없습니다.", fullSrc);
                }

                var destFull = Path.GetFullPath(destFolder);
                var destPath = GetUniqueDestinationPath(destFull, Path.GetFileName(fullSrc));
                var produced = action(fullSrc, destPath, ct);
                results.Add(produced);
                succeeded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add((src, ex.Message));
            }

            ReportProgress(progress, i + 1, total);
        }

        return new FileOpResult(succeeded, failures, results);
    }

    private static bool EnsureDestinationFolder(
        string destFolder,
        IReadOnlyList<string> files,
        List<(string Path, string Error)> failures)
    {
        try
        {
            Directory.CreateDirectory(destFolder);
            return true;
        }
        catch (Exception ex)
        {
            foreach (var f in files)
            {
                failures.Add((f, ex.Message));
            }
            return false;
        }
    }

    private static string GetUniqueDestinationPath(string destFolder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "file";
        }

        var dest = Path.Combine(destFolder, fileName);
        if (!File.Exists(dest) && !Directory.Exists(dest))
        {
            return dest;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        int n = 1;
        while (true)
        {
            var candidate = Path.Combine(destFolder, $"{name} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
            n++;
        }
    }

    private static bool IsSameDrive(string srcPath, string destFolder)
    {
        try
        {
            var srcRoot = Path.GetPathRoot(Path.GetFullPath(srcPath));
            var dstRoot = Path.GetPathRoot(Path.GetFullPath(destFolder));
            return string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ReportProgress(IProgress<int>? progress, int completed, int total)
    {
        if (progress is null)
        {
            return;
        }

        if (total <= 0)
        {
            progress.Report(100);
            return;
        }

        progress.Report(completed * 100 / total);
    }
}
