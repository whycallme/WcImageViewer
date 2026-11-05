namespace WcImageViewer.Models;

public enum ListSortKey
{
    Name,
    Modified,
    Size,
    Extension
}

public enum ListPrefsScope
{
    Global,
    PerFolder
}

public sealed class ListViewPrefs
{
    public int ThumbSize { get; set; } = 160;
    public ListSortKey SortKey { get; set; } = ListSortKey.Name;
    public bool SortDescending { get; set; }
}

public sealed class ListViewLayout
{
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool Maximized { get; set; }
    public double LeftColumnWidth { get; set; } = 260;
    public double LeftTopHeightRatio { get; set; } = 0.33;
    public double InfoHeight { get; set; } = 160;

    internal static int ComputePreviewDecodeWidth(double w, double h, double dpiScale)
    {
        var max = Math.Max(w, h);
        var scaled = max * dpiScale;
        var units = Math.Ceiling(scaled / 256.0);
        var width = (int)(units * 256.0);
        return Math.Clamp(width, 256, 4096);
    }
}
