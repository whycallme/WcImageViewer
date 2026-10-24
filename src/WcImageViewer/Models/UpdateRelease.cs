namespace WcImageViewer.Models;

public sealed class UpdateRelease
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string AssetUrl { get; set; } = string.Empty;
    public long AssetSize { get; set; }
    public string AssetDigest { get; set; } = string.Empty;
}
