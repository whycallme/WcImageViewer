using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class SettingsWindowSizingTests
{
    private const double WorkWidth = 1920;
    private const double WorkHeight = 1040;

    [Fact]
    public void ValidSavedSizeIsRestored()
    {
        var size = SettingsWindowSizing.Resolve(900, 700, WorkWidth, WorkHeight);

        Assert.Equal(900, size.Width);
        Assert.Equal(700, size.Height);
    }

    [Fact]
    public void MissingSizeUsesDefault()
    {
        var size = SettingsWindowSizing.Resolve(null, null, WorkWidth, WorkHeight);

        Assert.Equal(SettingsWindowSizing.DefaultWidth, size.Width);
        Assert.Equal(SettingsWindowSizing.DefaultHeight, size.Height);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-300)]
    [InlineData(100)]
    [InlineData(5000)]
    public void AbnormalSizeFallsBackToDefault(double bad)
    {
        var size = SettingsWindowSizing.Resolve(bad, bad, WorkWidth, WorkHeight);

        Assert.Equal(SettingsWindowSizing.DefaultWidth, size.Width);
        Assert.Equal(SettingsWindowSizing.DefaultHeight, size.Height);
    }

    [Fact]
    public void SizeLargerThanWorkAreaFallsBackToDefaultInsideScreen()
    {
        // 저장값이 작업 영역(1024x600)을 넘으면 기본값을 쓰되, 기본값도 영역 안으로 줄인다.
        var size = SettingsWindowSizing.Resolve(3000, 2000, 1024, 600);

        Assert.Equal(SettingsWindowSizing.DefaultWidth, size.Width);
        Assert.Equal(600, size.Height);
    }

    [Fact]
    public void TinyWorkAreaKeepsMinimumSize()
    {
        var size = SettingsWindowSizing.Resolve(null, null, 500, 300);

        Assert.Equal(SettingsWindowSizing.MinWidth, size.Width);
        Assert.Equal(SettingsWindowSizing.MinHeight, size.Height);
    }
}
