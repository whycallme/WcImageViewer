using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class ReleaseNotesFormatterTests
{
    [Fact]
    public void EmptyInputReturnsEmptyText()
    {
        Assert.Equal(string.Empty, ReleaseNotesFormatter.ToPlainText(null));
        Assert.Equal(string.Empty, ReleaseNotesFormatter.ToPlainText("   \n\n"));
    }

    [Fact]
    public void HeadingsAndEmphasisAreFlattened()
    {
        var text = ReleaseNotesFormatter.ToPlainText(
            "# WC Image Viewer v0.4.0\n\n## 주요 변경\n\n- **자동 업데이트** 확인과 `건너뛰기`\n- __네이티브__ updater");

        Assert.Equal(
            "WC Image Viewer v0.4.0\n\n주요 변경\n\n• 자동 업데이트 확인과 건너뛰기\n• 네이티브 updater",
            text);
        Assert.DoesNotContain("#", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("`", text);
    }

    [Fact]
    public void BlockquotesLoseTheirMarker()
    {
        var text = ReleaseNotesFormatter.ToPlainText("> **중요**: 한 번만 수동 설치\n> 두 번째 줄");

        Assert.Equal("중요: 한 번만 수동 설치\n두 번째 줄", text);
        Assert.DoesNotContain(">", text);
    }

    [Fact]
    public void LinksKeepOnlyTheirText()
    {
        var text = ReleaseNotesFormatter.ToPlainText(
            "자세한 내용은 [릴리즈 페이지](https://example.test/v0.4.0)를 보세요. ![배지](https://example.test/b.svg)");

        Assert.Equal("자세한 내용은 릴리즈 페이지를 보세요. 배지", text);
    }

    [Fact]
    public void TablesLosePipesAndSeparatorRows()
    {
        var text = ReleaseNotesFormatter.ToPlainText(
            "| 파일 | 크기 |\n|---|---|\n| `portable.zip` | 62 MB |\n| lite.zip | 3 MB |");

        Assert.Equal(
            "파일 · 크기\nportable.zip · 62 MB\nlite.zip · 3 MB",
            text);
        Assert.DoesNotContain("|", text);
    }

    [Fact]
    public void CodeFenceBlockIsDropped()
    {
        var text = ReleaseNotesFormatter.ToPlainText(
            "SHA-256\n\n```text\nABC123  portable.zip\nDEF456  lite.zip\n```\n\n끝");

        Assert.Equal("SHA-256\n\n끝", text);
    }

    [Fact]
    public void BlankLinesAreCollapsed()
    {
        var text = ReleaseNotesFormatter.ToPlainText("하나\n\n\n\n둘\n\n\n\n\n셋\n");

        Assert.Equal("하나\n\n둘\n\n셋", text);
    }
}
