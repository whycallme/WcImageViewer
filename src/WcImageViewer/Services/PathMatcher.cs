using System;
using System.Collections.Generic;
using System.IO;

namespace WcImageViewer.Services;

/// <summary>
/// 경로 비교용 정규화와 대소문자 무시 매칭. 폴더 트리 경로 매칭과 리스트 선택 대상 탐색이 같은 규칙을 쓰도록 한 곳에 둔다.
/// 정규화는 비교에만 쓰며, 노드·아이템이 가진 실제 경로 값은 바꾸지 않는다.
/// </summary>
public static class PathMatcher
{
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    /// <summary>전체 경로화 후 끝의 구분자를 제거한다(드라이브 루트 "C:\" 는 유지). 빈 입력은 빈 문자열.</summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch
        {
            full = path.Trim();
        }

        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && full.Length <= root.Length)
        {
            return full;
        }

        return full.TrimEnd(Separators);
    }

    public static bool AreEqual(string? a, string? b)
    {
        var na = Normalize(a);
        return na.Length > 0 && string.Equals(na, Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>paths 에서 target 과 (정규화 후, 대소문자 무시로) 같은 첫 항목의 인덱스. 없으면 -1.</summary>
    public static int IndexOf(IReadOnlyList<string> paths, string? target)
    {
        var t = Normalize(target);
        if (t.Length == 0)
        {
            return -1;
        }

        for (var i = 0; i < paths.Count; i++)
        {
            if (string.Equals(Normalize(paths[i]), t, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
