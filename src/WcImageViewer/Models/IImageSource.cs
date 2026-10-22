using System.IO;

namespace WcImageViewer.Models;

/// <summary>A uniform view over a folder or an archive.</summary>
public interface IImageSource : IDisposable
{
    string Id { get; }

    string DisplayName { get; }

    int Count { get; }

    ImageEntry GetEntry(int index);

    Task<Stream> OpenEntryAsync(int index, CancellationToken cancellationToken);
}
