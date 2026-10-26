using System.IO;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

public sealed class FolderImageSource : IImageSource
{
    private readonly ImageEntry[] _entries;

    private FolderImageSource(string displayName, string id, IEnumerable<string> files)
    {
        DisplayName = displayName;
        Id = id;
        _entries = files.Select(CreateEntry).ToArray();
    }

    public string Id { get; }

    public string DisplayName { get; }

    public int Count => _entries.Length;

    public ImageEntry GetEntry(int index) => _entries[index];

    public Task<Stream> OpenEntryAsync(int index, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = _entries[index].Key;
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Func<Stream>? CreateReopenFactory(int index) => () => ImageLoader.OpenFile(_entries[index].Key);

    public string? GetFilePath(int index) => _entries[index].Key;

    public int IndexOf(string filePath)
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            if (string.Equals(_entries[i].Key, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public void Dispose()
    {
    }

    public static FolderImageSource Create(string folder)
    {
        var files = ImageFileEnumerator.Enumerate(folder);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return new FolderImageSource(
            string.IsNullOrEmpty(name) ? folder : name,
            Path.GetFullPath(folder),
            files);
    }

    public static FolderImageSource CreateForFile(string file)
    {
        return new FolderImageSource(
            Path.GetFileName(file),
            Path.GetFullPath(file),
            new[] { file });
    }

    private static ImageEntry CreateEntry(string path)
    {
        long size = 0;
        DateTime? modified = null;

        try
        {
            var info = new FileInfo(path);
            size = info.Length;
            modified = info.LastWriteTimeUtc;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new ImageEntry(Path.GetFileName(path), path, size, modified);
    }
}
