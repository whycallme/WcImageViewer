namespace WcImageViewer.Models;

public sealed class UpdatePackage
{
    public string AppPath { get; set; } = string.Empty;

    // null when the ZIP has no updater; the embedded updater is used in that case.
    public string? UpdaterPath { get; set; }

    public string StagingDirectory { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
}
