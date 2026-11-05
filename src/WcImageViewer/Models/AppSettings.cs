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
    public string Theme { get; set; } = "System";
    public Dictionary<string, string> Shortcuts { get; set; } = new();
    public List<string> Bookmarks { get; set; } = new();

    public ListPrefsScope ListViewScope { get; set; } = ListPrefsScope.Global;
    public ListViewPrefs ListViewGlobal { get; set; } = new();
    public Dictionary<string, ListViewPrefs> ListViewFolderPrefs { get; set; } = new();

    public ListViewLayout ListViewLayout { get; set; } = new();

    public double UiFontSize { get; set; } = 12;
    public double ListViewItemFontSize { get; set; } = 12;

    public double? SettingsWindowWidth { get; set; }
    public double? SettingsWindowHeight { get; set; }

    public bool AutoCheckUpdates { get; set; } = true;
    public string SkippedVersion { get; set; } = string.Empty;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public UpdateRelease? PendingUpdate { get; set; }

    public List<string> RecentPaths { get; set; } = new();
}
