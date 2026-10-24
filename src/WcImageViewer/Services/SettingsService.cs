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

        if (settings.FitWindowToImage)
        {
            // Older builds auto-resized the window on every image, which could
            // leave a tiny window and an unexpected 100% zoom. Reset that state,
            // then use the new sane auto-fit behavior.
            settings.FitWindowToImage = false;
            settings.DefaultZoomPercent = 0;
            settings.WindowWidth = 1200;
            settings.WindowHeight = 800;
            settings.WindowLeft = null;
            settings.WindowTop = null;
            settings.AutoFitWindowToImage = true;
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
        settings.Theme = string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
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

        return settings;
    }
}
