namespace WcImageViewer.Models;

public sealed class UpdatePackage
{
    public string AppPath { get; set; } = string.Empty;
    public string UpdaterPath { get; set; } = string.Empty;
    public string StagingDirectory { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
}
