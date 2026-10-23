using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WcImageViewer.Services;

public static class FileAssociationService
{
    private const string ProgIdPrefix = "WcImageViewer";
    private const string LegacyImageProgId = "WcImageViewer.Image";
    private const string LegacyArchiveProgId = "WcImageViewer.Archive";
    private const string ApplicationKey = @"Applications\WcImageViewer.exe";
    private const string BackupKey = "WcImageViewer.Backup";
    private const string NoPreviousAssociation = "__WCIV_NONE__";
    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0;

    public static IReadOnlyList<string> ImageExtensions { get; } = new[]
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico",
        ".webp", ".avif", ".heic", ".heif"
    };

    public static IReadOnlyList<string> ArchiveExtensions { get; } = new[]
    {
        ".zip", ".cbz", ".7z", ".cb7", ".rar", ".cbr"
    };

    public static IReadOnlyList<string> SupportedExtensions { get; } =
        ImageExtensions.Concat(ArchiveExtensions).ToArray();

    public static void Apply(IEnumerable<string> extensions, string executablePath)
    {
        var selected = new HashSet<string>(
            extensions
                .Select(NormalizeExtension)
                .Where(SupportedExtensions.Contains),
            StringComparer.OrdinalIgnoreCase);

        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes")
            ?? throw new IOException("레지스트리 키를 열 수 없습니다.");

        if (selected.Count == 0)
        {
            RemoveAll(classes);
            NotifyShell();
            return;
        }

        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in selected)
        {
            var iconPath = FileIconService.Extract(extension);
            if (iconPath is not null)
            {
                icons[extension] = iconPath;
            }
        }

        foreach (var extension in SupportedExtensions)
        {
            if (selected.Contains(extension))
            {
                RegisterExtension(classes, extension, executablePath, icons.GetValueOrDefault(extension));
            }
            else
            {
                RemoveExtension(classes, extension);
            }
        }

        // Older versions used two shared ProgIDs; replace them with the per-extension ones.
        classes.DeleteSubKeyTree(LegacyImageProgId, throwOnMissingSubKey: false);
        classes.DeleteSubKeyTree(LegacyArchiveProgId, throwOnMissingSubKey: false);

        using var application = classes.CreateSubKey(ApplicationKey + @"\shell\open\command");
        application?.SetValue(null, $"\"{executablePath}\" \"%1\"");

        NotifyShell();
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        Apply(enabled ? SupportedExtensions : Array.Empty<string>(), executablePath);
    }

    public static string GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path) &&
            !string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var appHost = Path.Combine(AppContext.BaseDirectory, "WcImageViewer.exe");
        return File.Exists(appHost) ? appHost : path ?? string.Empty;
    }

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim().ToLowerInvariant();
        return value.StartsWith('.') ? value : "." + value;
    }

    private static string ProgIdFor(string extension)
    {
        return ProgIdPrefix + extension;
    }

    private static void RegisterExtension(
        RegistryKey classes,
        string extension,
        string executablePath,
        string? iconPath)
    {
        var progId = ProgIdFor(extension);
        var friendlyName = FriendlyName(extension);

        using (var key = classes.CreateSubKey(progId))
        {
            if (key is not null)
            {
                key.SetValue(null, friendlyName);
                key.SetValue("FriendlyTypeName", friendlyName);

                using var icon = key.CreateSubKey("DefaultIcon");
                icon?.SetValue(null, iconPath is null
                    ? $"\"{executablePath}\",0"
                    : $"\"{iconPath}\",0");

                using var command = key.CreateSubKey(@"shell\open\command");
                command?.SetValue(null, $"\"{executablePath}\" \"%1\"");
            }
        }

        SetExtension(classes, extension, progId);
    }

    private static string FriendlyName(string extension)
    {
        var label = extension.TrimStart('.').ToUpperInvariant();
        return ImageExtensions.Contains(extension)
            ? $"{label} 이미지"
            : $"{label} 압축 파일";
    }

    private static void SetExtension(RegistryKey classes, string extension, string progId)
    {
        using var key = classes.CreateSubKey(extension);
        if (key is null)
        {
            return;
        }

        var previous = key.GetValue(null) as string;
        if (!string.Equals(previous, progId, StringComparison.OrdinalIgnoreCase))
        {
            using var backup = classes.CreateSubKey(BackupKey);
            if (backup is not null && backup.GetValue(extension) is null)
            {
                backup.SetValue(extension, previous ?? NoPreviousAssociation);
            }

            key.SetValue(null, progId);
        }

        using var openWith = key.CreateSubKey("OpenWithProgids");
        openWith?.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
        openWith?.DeleteValue(LegacyImageProgId, throwOnMissingValue: false);
        openWith?.DeleteValue(LegacyArchiveProgId, throwOnMissingValue: false);
    }

    private static void RemoveExtension(RegistryKey classes, string extension)
    {
        using var key = classes.OpenSubKey(extension, writable: true);
        if (key is not null)
        {
            var current = key.GetValue(null) as string;
            if (IsOurProgId(current, extension))
            {
                using var backup = classes.OpenSubKey(BackupKey, writable: true);
                var previous = backup?.GetValue(extension) as string;
                if (string.IsNullOrEmpty(previous) || previous == NoPreviousAssociation)
                {
                    key.DeleteValue(string.Empty, throwOnMissingValue: false);
                }
                else
                {
                    key.SetValue(null, previous);
                }

                backup?.DeleteValue(extension, throwOnMissingValue: false);
            }

            using var openWith = key.OpenSubKey("OpenWithProgids", writable: true);
            openWith?.DeleteValue(ProgIdFor(extension), throwOnMissingValue: false);
            openWith?.DeleteValue(LegacyImageProgId, throwOnMissingValue: false);
            openWith?.DeleteValue(LegacyArchiveProgId, throwOnMissingValue: false);
        }

        classes.DeleteSubKeyTree(ProgIdFor(extension), throwOnMissingSubKey: false);
    }

    private static bool IsOurProgId(string? value, string extension)
    {
        return string.Equals(value, ProgIdFor(extension), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, LegacyImageProgId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, LegacyArchiveProgId, StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveAll(RegistryKey classes)
    {
        foreach (var extension in SupportedExtensions)
        {
            RemoveExtension(classes, extension);
        }

        classes.DeleteSubKeyTree(LegacyImageProgId, throwOnMissingSubKey: false);
        classes.DeleteSubKeyTree(LegacyArchiveProgId, throwOnMissingSubKey: false);
        classes.DeleteSubKeyTree(ApplicationKey, throwOnMissingSubKey: false);
        classes.DeleteSubKeyTree(BackupKey, throwOnMissingSubKey: false);
    }

    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Refresh is a convenience; the registry change is already applied.
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
