using System.IO;
using System.Text.Json;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    private SettingsService(string filePath, AppSettings settings)
    {
        _filePath = filePath;
        Settings = settings;
    }

    public string FilePath => _filePath;

    public AppSettings Settings { get; }

    public static SettingsService LoadDefault()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WcImageViewer");
        var path = Path.Combine(settingsDirectory, "settings.json");

        if (!File.Exists(path))
        {
            var legacyPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "settings.json"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WcImageViewer",
                    "settings.json")
            };

            foreach (var legacyPath in legacyPaths)
            {
                if (!File.Exists(legacyPath))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(settingsDirectory);
                    File.Copy(legacyPath, path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                break;
            }
        }

        return ForPath(path);
    }

    public static SettingsService ForPath(string filePath)
    {
        return new SettingsService(filePath, Load(filePath));
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Settings, JsonOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private static AppSettings Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath));
                if (settings is not null)
                {
                    return Normalize(settings);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }

        return Normalize(new AppSettings());
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (settings.Version < 2)
        {
            // Thumbnails used to be pinned by default. New default is hidden
            // until the pointer reaches the bottom edge or T is pressed.
            settings.ThumbnailsVisible = false;
            settings.Version = 2;
        }

        settings.RecentPaths ??= new List<string>();
        settings.RecentPaths = settings.RecentPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        settings.AssociatedExtensions ??= new List<string>();
        settings.AssociatedExtensions = settings.AssociatedExtensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension =>
            {
                var value = extension.Trim().ToLowerInvariant();
                return value.StartsWith('.') ? value : "." + value;
            })
            .Where(extension => FileAssociationService.SupportedExtensions
                .Any(supported => string.Equals(supported, extension, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.AssociateFileTypes && settings.AssociatedExtensions.Count == 0)
        {
            settings.AssociatedExtensions = FileAssociationService.SupportedExtensions.ToList();
        }

        settings.AssociateFileTypes = false;
        // 기존 저장값(Light/Dark)은 그대로, 값이 없거나 알 수 없으면 시스템.
        settings.Theme = ThemeService.ToSetting(ThemeService.ParseMode(settings.Theme));
        settings.Shortcuts ??= new Dictionary<string, string>();
        settings.Bookmarks ??= new List<string>();
        settings.SlideshowIntervalSeconds = Math.Clamp(settings.SlideshowIntervalSeconds, 2, 60);
        settings.DefaultZoomPercent = settings.DefaultZoomPercent <= 0
            ? 0
            : Math.Clamp(settings.DefaultZoomPercent, 5, 800);

        settings.SkippedVersion = UpdateService.ParseTagVersion(settings.SkippedVersion) is { } skippedVersion
            ? UpdateService.FormatVersion(skippedVersion)
            : string.Empty;

        if (settings.LastUpdateCheckUtc is DateTime lastCheck)
        {
            settings.LastUpdateCheckUtc = lastCheck.Kind == DateTimeKind.Utc
                ? lastCheck
                : lastCheck.ToUniversalTime();
        }

        if (settings.PendingUpdate is not null)
        {
            var pendingVersion = UpdateService.ParseTagVersion(settings.PendingUpdate.Tag);
            if (pendingVersion is null || pendingVersion <= AppVersion.Current)
            {
                settings.PendingUpdate = null;
            }
        }

        settings.ListViewGlobal ??= new ListViewPrefs();
        settings.ListViewGlobal.ThumbSize = Math.Clamp(settings.ListViewGlobal.ThumbSize, 64, 320);

        settings.ListViewFolderPrefs ??= new Dictionary<string, ListViewPrefs>();
        foreach (var key in settings.ListViewFolderPrefs.Keys.ToList())
        {
            var value = settings.ListViewFolderPrefs[key];
            if (value is null)
            {
                settings.ListViewFolderPrefs[key] = new ListViewPrefs();
            }
            else
            {
                value.ThumbSize = Math.Clamp(value.ThumbSize, 64, 320);
            }
        }

        settings.ListViewLayout ??= new ListViewLayout();
        settings.ListViewLayout.LeftColumnWidth = Math.Clamp(settings.ListViewLayout.LeftColumnWidth, 180, 4000);
        settings.ListViewLayout.LeftTopHeightRatio = Math.Clamp(settings.ListViewLayout.LeftTopHeightRatio, 0.1, 0.9);
        settings.ListViewLayout.InfoHeight = Math.Clamp(settings.ListViewLayout.InfoHeight, 80, 500);

        settings.UiFontSize = Math.Clamp(settings.UiFontSize, 8, 48);
        settings.ListViewItemFontSize = Math.Clamp(settings.ListViewItemFontSize, 8, 48);

        return settings;
    }
}
