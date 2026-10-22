using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WcImageViewer.Services;

/// <summary>Loads the per-extension icon assets that are embedded in the executable.</summary>
public static class FileIconService
{
    private const string ResourcePrefix = "pack://application:,,,/Assets/FileIcons/";
    private const string FolderName = "FileIcons";

    /// <summary>Extracts the icon next to the user settings so Explorer can read it.</summary>
    public static string? Extract(string extension)
    {
        var name = IconName(extension);
        if (name is null)
        {
            return null;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WcImageViewer",
            FolderName);
        var path = Path.Combine(folder, name + ".ico");

        try
        {
            Directory.CreateDirectory(folder);
            var resource = Application.GetResourceStream(new Uri(ResourcePrefix + name + ".ico"));
            if (resource is null)
            {
                return File.Exists(path) ? path : null;
            }

            using (resource.Stream)
            {
                if (File.Exists(path) && new FileInfo(path).Length == resource.Stream.Length)
                {
                    return path;
                }

                using var target = File.Create(path);
                resource.Stream.CopyTo(target);
            }

            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or InvalidOperationException or NotSupportedException or UriFormatException)
        {
            // Keep the association usable even if the icon cannot be written.
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>Loads an icon for the settings window.</summary>
    public static BitmapImage? Load(string extension)
    {
        var name = IconName(extension);
        if (name is null)
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(ResourcePrefix + name + ".ico");
            image.DecodePixelWidth = 32;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException
                                       or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    private static string? IconName(string extension)
    {
        var name = extension.Trim().TrimStart('.').ToLowerInvariant();
        return name.Length > 0 && name.All(char.IsLetterOrDigit) ? name : null;
    }
}
