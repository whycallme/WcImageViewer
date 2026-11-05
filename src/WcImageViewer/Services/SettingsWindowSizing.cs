namespace WcImageViewer.Services;

/// <summary>설정 창 크기 결정 로직 (순수 함수, 단위 테스트 대상).</summary>
public static class SettingsWindowSizing
{
    public const double DefaultWidth = 760;
    public const double DefaultHeight = 620;
    public const double MinWidth = 640;
    public const double MinHeight = 440;

    /// <summary>
    /// 저장된 크기를 검증한다. 값이 없거나, 유한하지 않거나, 최소 크기 미만이거나,
    /// 작업 영역(화면)을 넘으면 기본값을 쓴다. 기본값도 작업 영역보다 크면 작업 영역 안으로 줄인다.
    /// </summary>
    public static (double Width, double Height) Resolve(
        double? savedWidth,
        double? savedHeight,
        double workAreaWidth,
        double workAreaHeight)
    {
        return (
            Pick(savedWidth, DefaultWidth, MinWidth, workAreaWidth),
            Pick(savedHeight, DefaultHeight, MinHeight, workAreaHeight));
    }

    private static double Pick(double? saved, double fallback, double min, double workArea)
    {
        var limit = IsUsable(workArea) ? Math.Max(min, workArea) : double.PositiveInfinity;
        if (saved is double value && double.IsFinite(value) && value >= min && value <= limit)
        {
            return value;
        }

        return Math.Min(fallback, limit);
    }

    private static bool IsUsable(double value)
    {
        return double.IsFinite(value) && value > 0;
    }
}
