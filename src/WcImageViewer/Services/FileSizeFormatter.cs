namespace WcImageViewer.Services;

/// <summary>
/// 파일 용량을 사람이 읽는 단위(B/KB/MB/GB)로 포맷한다.
/// MainWindow.FormatFileSize 와 동일한 기준을 사용한다.
/// </summary>
public static class FileSizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kb = bytes / 1024.0;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        double mb = kb / 1024.0;
        if (mb < 1024)
        {
            return $"{mb:0.##} MB";
        }

        double gb = mb / 1024.0;
        return $"{gb:0.##} GB";
    }
}
