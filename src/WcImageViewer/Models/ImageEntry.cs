using System.IO;

namespace WcImageViewer.Models;

public sealed record ImageEntry(
    string Name,
    string Key,
    long Size,
    DateTime? LastModifiedUtc)
{
    public string DisplayName => Path.GetFileName(Name.Replace('/', Path.DirectorySeparatorChar));
}
