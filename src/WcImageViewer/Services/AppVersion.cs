namespace WcImageViewer.Services;

public static class AppVersion
{
    public static Version Current { get; } =
        typeof(AppVersion).Assembly.GetName().Version ?? new Version(0, 3, 0, 0);

    public static string Display => $"{Current.Major}.{Current.Minor}.{Math.Max(Current.Build, 0)}";

#if LITE
    public const bool IsLite = true;
#else
    public const bool IsLite = false;
#endif

    public static string Flavor => IsLite ? "lite" : "portable";
}
