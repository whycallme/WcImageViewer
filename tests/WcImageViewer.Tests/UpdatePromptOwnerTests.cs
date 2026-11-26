using System;
using WcImageViewer;
using Xunit;

namespace WcImageViewer.Tests;

public class UpdatePromptOwnerTests
{
    [Fact]
    public void HiddenWindow_CannotBeUsedAsOwner()
    {
        Assert.False(MainWindow.CanUseAsUpdatePromptOwner(false, new IntPtr(12345)));
    }

    [Fact]
    public void VisibleWindowWithoutHandle_CannotBeUsedAsOwner()
    {
        Assert.False(MainWindow.CanUseAsUpdatePromptOwner(true, IntPtr.Zero));
    }

    [Fact]
    public void VisibleWindowWithHandle_CanBeUsedAsOwner()
    {
        Assert.True(MainWindow.CanUseAsUpdatePromptOwner(true, new IntPtr(12345)));
    }
}
