using WcImageViewer.Controls;

namespace WcImageViewer.Tests;

public class ThumbnailListControlEnterTests
{
    [Fact]
    public void UsesFocusedPathWhenAvailable()
    {
        var item = new ThumbItem("selected.png", "selected.png");
        Assert.Equal("focused.png", ThumbnailListControl.ResolveEnterActivationPath("focused.png", item));
    }

    [Fact]
    public void FallsBackToSelectedItemWhenFocusedPathNull()
    {
        var item = new ThumbItem("selected.png", "selected.png");
        Assert.Equal("selected.png", ThumbnailListControl.ResolveEnterActivationPath(null, item));
    }

    [Fact]
    public void ReturnsNullWhenBothMissing()
    {
        Assert.Null(ThumbnailListControl.ResolveEnterActivationPath(null, null));
    }
}