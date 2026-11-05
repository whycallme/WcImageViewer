using System.IO;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

public static class ListSorter
{
    public sealed record ImageEntry(string Path, string Name, long Size, DateTime Modified, string Extension);

    public static ImageEntry FromFile(string path)
    {
        var name = Path.GetFileName(path);
        long size = 0;
        var modified = default(DateTime);
        var extension = Path.GetExtension(path);

        try
        {
            var info = new FileInfo(path);
            name = info.Name;
            size = info.Length;
            modified = info.LastWriteTimeUtc;
            extension = info.Extension;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        return new ImageEntry(path, name, size, modified, extension);
    }

    public static IReadOnlyList<ImageEntry> Sort(IEnumerable<ImageEntry> items, ListSortKey key, bool desc)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var source = items.ToList();

        IEnumerable<ImageEntry> ordered = key switch
        {
            ListSortKey.Name => desc
                ? source.OrderByDescending(item => item.Name, NaturalStringComparer.Instance)
                : source.OrderBy(item => item.Name, NaturalStringComparer.Instance),
            ListSortKey.Modified => desc
                ? source.OrderByDescending(item => item.Modified)
                : source.OrderBy(item => item.Modified),
            ListSortKey.Size => desc
                ? source.OrderByDescending(item => item.Size)
                : source.OrderBy(item => item.Size),
            ListSortKey.Extension => desc
                ? source.OrderByDescending(item => item.Extension, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(item => item.Extension, StringComparer.OrdinalIgnoreCase),
            _ => source
        };

        return ordered.ToList();
    }
}