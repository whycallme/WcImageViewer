using System.Text;
using System.Text.RegularExpressions;

namespace WcImageViewer.Services;

/// <summary>
/// GitHub 릴리즈 본문(Markdown)을 업데이트 창에 표시할 평문으로 바꾼다.
/// 전체 Markdown 파서가 아니라 릴리즈 노트에 쓰이는 최소 문법만 처리한다.
/// </summary>
public static class ReleaseNotesFormatter
{
    private static readonly Regex LinkPattern = new(@"!?\[(?<text>[^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex HeadingPattern = new(@"^\s{0,3}#{1,6}\s*", RegexOptions.Compiled);
    private static readonly Regex QuotePattern = new(@"^(?:\s{0,3}>\s?)+", RegexOptions.Compiled);
    private static readonly Regex BulletPattern = new(@"^(\s*)[-*]\s+", RegexOptions.Compiled);
    private static readonly Regex RulePattern = new(@"^\s{0,3}(?<char>[-*_])(\s*\k<char>){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex TableSeparatorPattern = new(@"^\s*\|?(\s*:?-{2,}:?\s*\|)+\s*$", RegexOptions.Compiled);
    private static readonly Regex BlankRunPattern = new(@"\n{3,}", RegexOptions.Compiled);

    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var inFence = false;

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence || RulePattern.IsMatch(line) || TableSeparatorPattern.IsMatch(line))
            {
                continue;
            }

            if (trimmed.StartsWith('|'))
            {
                line = string.Join(
                    " · ",
                    trimmed.Trim('|')
                        .Split('|')
                        .Select(cell => cell.Trim())
                        .Where(cell => cell.Length > 0));
            }

            line = HeadingPattern.Replace(line, string.Empty);
            line = QuotePattern.Replace(line, string.Empty);
            line = BulletPattern.Replace(line, "$1• ");
            line = LinkPattern.Replace(line, "${text}");
            line = line.Replace("**", string.Empty)
                .Replace("__", string.Empty)
                .Replace("`", string.Empty)
                .TrimEnd();
            builder.Append(line).Append('\n');
        }

        return BlankRunPattern.Replace(builder.ToString().Trim(), "\n\n");
    }
}
