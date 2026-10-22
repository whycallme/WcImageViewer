using System.IO;

namespace WcImageViewer.Services;

public static class ImageFileEnumerator
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico"
    };

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path));

    public static IReadOnlyList<string> Enumerate(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = FileAttributes.System
            })
            .Where(IsImage)
            .OrderBy(path => Path.GetFileName(path), NaturalStringComparer.Instance)
            .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Array.Empty<string>();
        }
    }
}
