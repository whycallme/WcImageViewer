using System.IO;
using System.IO.Compression;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class ArchiveImageSourceTests
{
    [Fact]
    public async Task OpensZipAndDecodesEntry()
    {
        var root = CreateRoot();
        try
        {
            var imagesFolder = Path.Combine(root, "images");
            Directory.CreateDirectory(imagesFolder);
            TestImages.CreatePng(Path.Combine(imagesFolder, "1.png"), 4, 3);
            TestImages.CreatePng(Path.Combine(imagesFolder, "2.png"), 5, 3);
            TestImages.CreatePng(Path.Combine(imagesFolder, "10.png"), 6, 3);

            var archivePath = Path.Combine(root, "test.zip");
            ZipFile.CreateFromDirectory(imagesFolder, archivePath, CompressionLevel.NoCompression, false);

            using var source = await ArchiveImageSource.CreateAsync(
                archivePath,
                null,
                CancellationToken.None);

            Assert.Equal(3, source.Count);
            Assert.Equal(
                new[] { "1.png", "2.png", "10.png" },
                Enumerable.Range(0, source.Count).Select(i => source.GetEntry(i).Name));

            using var stream = await source.OpenEntryAsync(0, CancellationToken.None);
            var document = StaRunner.Run(() => ImageLoader.LoadDocument(stream));

            Assert.Equal(4, document.Image.PixelWidth);
            Assert.Equal(3, document.Image.PixelHeight);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpensSevenZipAndExtractsEntries()
    {
        var root = CreateRoot();
        var archivePath = Path.Combine(root, "test.7z");
        string cacheDirectory = null!;

        try
        {
            var imagesFolder = Path.Combine(root, "images");
            Directory.CreateDirectory(imagesFolder);
            TestImages.CreatePng(Path.Combine(imagesFolder, "1.png"), 4, 3);
            TestImages.CreatePng(Path.Combine(imagesFolder, "2.png"), 5, 3);

            using (var output = File.Create(archivePath))
            using (var writer = WriterFactory.OpenWriter(
                       output,
                       ArchiveType.SevenZip,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            {
                foreach (var name in new[] { "1.png", "2.png" })
                {
                    using var input = File.OpenRead(Path.Combine(imagesFolder, name));
                    writer.Write(name, input, DateTime.UtcNow);
                }
            }

            cacheDirectory = ArchiveCache.GetDirectory(archivePath);

            using var source = await ArchiveImageSource.CreateAsync(
                archivePath,
                null,
                CancellationToken.None);

            Assert.Equal(2, source.Count);
            Assert.Equal(new[] { "1.png", "2.png" }, Enumerable.Range(0, source.Count).Select(i => source.GetEntry(i).Name));

            using var stream = await source.OpenEntryAsync(0, CancellationToken.None);
            var document = StaRunner.Run(() => ImageLoader.LoadDocument(stream));

            Assert.Equal(4, document.Image.PixelWidth);
            Assert.Equal(3, document.Image.PixelHeight);
        }
        finally
        {
            TryDeleteDirectory(cacheDirectory);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "wciv_archive_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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
