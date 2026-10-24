namespace WcImageViewer.Models;

public sealed class AppSettings
{
    public int Version { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool IsMaximized { get; set; }
    public bool ThumbnailsVisible { get; set; }
    public int SlideshowIntervalSeconds { get; set; } = 3;
    public bool SlideshowRepeat { get; set; } = true;
    public bool SlideshowRandom { get; set; }
    public int DefaultZoomPercent { get; set; }
    public bool AutoFitWindowToImage { get; set; } = true;
    public bool DoNotUpscaleSmallImages { get; set; } = true;
    public bool ConfirmDelete { get; set; } = true;
    public bool UseRecycleBin { get; set; } = true;
    public string EditorPath { get; set; } = string.Empty;
    public List<string> AssociatedExtensions { get; set; } = new();
    public bool AssociateFileTypes { get; set; }
    public bool SingleWindowMode { get; set; }
    public bool WrapNavigation { get; set; } = true;
    public bool ShowNavigationNotice { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public Dictionary<string, string> Shortcuts { get; set; } = new();
    public List<string> Bookmarks { get; set; } = new();

    // Legacy setting from an earlier build; migrated to AutoFitWindowToImage.
    public bool FitWindowToImage { get; set; }

    public List<string> RecentPaths { get; set; } = new();
}
