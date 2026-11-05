using System;
using System.Collections.Generic;
using System.IO;

namespace WcImageViewer.Services;

/// <summary>폴더 트리의 경로→노드 체인 계산(순수 함수, WPF 비의존).</summary>
public static class FolderTreeNavigation
{
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    /// <summary>
    /// root(드라이브 노드 경로)부터 target 까지의 경로 목록을 돌려준다. 첫 항목은 root 자신,
    /// 마지막 항목은 target. target 이 root 와 같거나 root 하위가 아니면 빈 목록.
    /// 반환 값은 정규화된 경로이며 비교는 대소문자를 무시한다.
    /// </summary>
    public static IReadOnlyList<string> BuildChain(string root, string target)
    {
        var rootN = PathMatcher.Normalize(root);
        var targetN = PathMatcher.Normalize(target);
        if (rootN.Length == 0 || targetN.Length == 0)
        {
            return Array.Empty<string>();
        }

        var prefix = rootN.EndsWith(Path.DirectorySeparatorChar) || rootN.EndsWith(Path.AltDirectorySeparatorChar)
            ? rootN
            : rootN + Path.DirectorySeparatorChar;

        var isSame = string.Equals(targetN, rootN, StringComparison.OrdinalIgnoreCase);
        if (!isSame && !targetN.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        var chain = new List<string> { rootN };
        if (isSame)
        {
            return chain;
        }

        var accumulated = rootN;
        var rest = targetN.Substring(prefix.Length);
        foreach (var seg in rest.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated = Path.Combine(accumulated, seg);
            chain.Add(accumulated);
        }

        return chain;
    }

    /// <summary>자식 노드 경로 목록에서 path 와 같은 노드의 인덱스(대소문자·정규화 무시). 없으면 -1.</summary>
    public static int IndexOfChild(IReadOnlyList<string> childPaths, string path) =>
        PathMatcher.IndexOf(childPaths, path);
}
