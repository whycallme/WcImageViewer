using System.IO;
using System.Linq;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class FileOperationServiceTests
{
    private static string NewTempDir() => TestTemp.CreateFolder();

    [Fact]
    public void Copy_CopiesFilesAndReturnsDestinationPaths()
    {
        var src = NewTempDir();
        var dst = NewTempDir();
        try
        {
            var a = Path.Combine(src, "a.txt");
            var b = Path.Combine(src, "b.txt");
            File.WriteAllText(a, "A");
            File.WriteAllText(b, "B");

            var result = FileOperationService.Copy(new[] { a, b }, dst);

            Assert.Equal(2, result.Succeeded);
            Assert.Empty(result.Failures);
            Assert.Equal(2, result.ResultPaths.Count);
            Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
            Assert.True(File.Exists(Path.Combine(dst, "b.txt")));
            Assert.True(File.Exists(a)); // original remains
        }
        finally
        {
            Directory.Delete(src, recursive: true);
            Directory.Delete(dst, recursive: true);
        }
    }

    [Fact]
    public void Move_MovesFiles()
    {
        var src = NewTempDir();
        var dst = NewTempDir();
        try
        {
            var a = Path.Combine(src, "a.txt");
            File.WriteAllText(a, "A");

            var result = FileOperationService.Move(new[] { a }, dst);

            Assert.Equal(1, result.Succeeded);
            Assert.Empty(result.Failures);
            Assert.False(File.Exists(a));
            Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
        }
        finally
        {
            Directory.Delete(src, recursive: true);
            Directory.Delete(dst, recursive: true);
        }
    }

    [Fact]
    public void Copy_NameConflict_GetsNumberedName()
    {
        var src = NewTempDir();
        var dst = NewTempDir();
        try
        {
            var a = Path.Combine(src, "a.txt");
            File.WriteAllText(a, "A");
            File.WriteAllText(Path.Combine(dst, "a.txt"), "existing");

            var result = FileOperationService.Copy(new[] { a }, dst);

            Assert.Equal(1, result.Succeeded);
            Assert.True(File.Exists(Path.Combine(dst, "a (1).txt")));
        }
        finally
        {
            Directory.Delete(src, recursive: true);
            Directory.Delete(dst, recursive: true);
        }
    }

    [Fact]
    public void Copy_MissingFile_DoesNotStopOthers()
    {
        var src = NewTempDir();
        var dst = NewTempDir();
        try
        {
            var a = Path.Combine(src, "a.txt");
            var missing = Path.Combine(src, "nope.txt");
            File.WriteAllText(a, "A");

            var result = FileOperationService.Copy(new[] { missing, a }, dst);

            Assert.Equal(1, result.Succeeded);
            Assert.Single(result.Failures);
            Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
        }
        finally
        {
            Directory.Delete(src, recursive: true);
            Directory.Delete(dst, recursive: true);
        }
    }

    [Fact]
    public void Move_SameFolder_IsSkippedAsFailure()
    {
        var folder = NewTempDir();
        try
        {
            var a = Path.Combine(folder, "a.txt");
            File.WriteAllText(a, "A");

            var result = FileOperationService.Move(new[] { a }, folder);

            Assert.Equal(0, result.Succeeded);
            Assert.Single(result.Failures);
            Assert.True(File.Exists(a));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Copy_Cancellation_Throws()
    {
        var src = NewTempDir();
        var dst = NewTempDir();
        try
        {
            var files = Enumerable.Range(0, 5).Select(i =>
            {
                var p = Path.Combine(src, $"f{i}.txt");
                File.WriteAllText(p, "x");
                return p;
            }).ToList();

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                FileOperationService.Copy(files, dst, ct: cts.Token));
        }
        finally
        {
            Directory.Delete(src, recursive: true);
            Directory.Delete(dst, recursive: true);
        }
    }

    [Fact]
    public void Delete_UseRecycleBinFalse_DeletesFiles()
    {
        var folder = NewTempDir();
        try
        {
            var a = Path.Combine(folder, "a.txt");
            var b = Path.Combine(folder, "b.txt");
            File.WriteAllText(a, "A");
            File.WriteAllText(b, "B");

            var result = FileOperationService.Delete(new[] { a, b }, false);

            Assert.Equal(2, result.Succeeded);
            Assert.Empty(result.Failures);
            Assert.False(File.Exists(a));
            Assert.False(File.Exists(b));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Copy_Progress_IsReported()
    {
        var src = NewTempDir();
        var dst = NewTempDir();
        try
        {
            var a = Path.Combine(src, "a.txt");
            File.WriteAllText(a, "A");
            var reported = new List<int>();
            var progress = new Progress<int>(v => reported.Add(v));

            FileOperationService.Copy(new[] { a }, dst, progress);

            Assert.NotEmpty(reported);
        }
        finally
        {
            Directory.Delete(src, recursive: true);
            Directory.Delete(dst, recursive: true);
        }
    }
}
