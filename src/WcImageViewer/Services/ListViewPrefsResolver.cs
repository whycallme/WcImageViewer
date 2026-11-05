using System.IO;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

public static class ListViewPrefsResolver
{
    public static ListViewPrefs Get(AppSettings s, string folder)
    {
        var key = Normalize(folder);
        if (s.ListViewScope == ListPrefsScope.PerFolder &&
            s.ListViewFolderPrefs is not null &&
            s.ListViewFolderPrefs.TryGetValue(key, out var prefs) &&
            prefs is not null)
        {
            return Clone(prefs);
        }

        return Clone(s.ListViewGlobal ?? new ListViewPrefs());
    }

    public static void Set(AppSettings s, string folder, ListViewPrefs p)
    {
        var prefs = Clone(p);
        prefs.ThumbSize = Math.Clamp(prefs.ThumbSize, 64, 320);

        if (s.ListViewScope == ListPrefsScope.Global)
        {
            s.ListViewGlobal = prefs;
            return;
        }

        var key = Normalize(folder);
        s.ListViewFolderPrefs ??= new Dictionary<string, ListViewPrefs>();

        if (!s.ListViewFolderPrefs.ContainsKey(key) && s.ListViewFolderPrefs.Count >= 500)
        {
            var firstKey = s.ListViewFolderPrefs.Keys.FirstOrDefault();
            if (firstKey is not null)
            {
                s.ListViewFolderPrefs.Remove(firstKey);
            }
        }

        s.ListViewFolderPrefs[key] = prefs;
    }

    private static string Normalize(string folder)
    {
        var full = Path.GetFullPath(folder);
        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.ToLowerInvariant();
    }

    private static ListViewPrefs Clone(ListViewPrefs p)
    {
        return new ListViewPrefs
        {
            ThumbSize = p.ThumbSize,
            SortKey = p.SortKey,
            SortDescending = p.SortDescending
        };
    }
}
