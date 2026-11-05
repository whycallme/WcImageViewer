using System.Collections.Generic;
using System.Linq;
using WcImageViewer.Controls;

namespace WcImageViewer.Tests;

public class ThumbnailSelectionTests
{
    private static readonly IReadOnlyList<string> Paths = new List<string>
    {
        @"C:\Pics\a.png",
        @"C:\Pics\IMG_0001.JPG",
        @"C:\Pics\b.png",
    };

    [Fact]
    public void ResolveSelectionIndex_ExactPath_ReturnsItsIndex()
    {
        Assert.Equal(2, ThumbnailListControl.ResolveSelectionIndex(Paths, @"C:\Pics\b.png"));
    }

    [Fact]
    public void ResolveSelectionIndex_DifferentCaseOnly_ReturnsItsIndex()
    {
        Assert.Equal(1, ThumbnailListControl.ResolveSelectionIndex(Paths, @"c:\pics\img_0001.jpg"));
    }

    [Fact]
    public void ResolveSelectionIndex_MissingOrNull_ReturnsMinusOne()
    {
        Assert.Equal(-1, ThumbnailListControl.ResolveSelectionIndex(Paths, @"C:\Pics\missing.png"));
        Assert.Equal(-1, ThumbnailListControl.ResolveSelectionIndex(Paths, null));
        Assert.Equal(-1, ThumbnailListControl.ResolveSelectionIndex(Array.Empty<string>(), @"C:\Pics\a.png"));
    }

    [Fact]
    public void SelectPath_DifferentCase_SelectsItemAndUsesCanonicalFocusedPath()
    {
        var result = StaRunner.Run(() =>
        {
            var control = new ThumbnailListControl();
            control.SetItems(Paths.Select(p => new ThumbItem(p, System.IO.Path.GetFileName(p))).ToList());

            var found = control.SelectPath(@"c:\pics\IMG_0001.jpg");
            return (Found: found, Selected: control.SelectedPaths.ToList(), Focused: control.FocusedPath);
        });

        Assert.True(result.Found);
        Assert.Equal(new[] { @"C:\Pics\IMG_0001.JPG" }, result.Selected);
        Assert.Equal(@"C:\Pics\IMG_0001.JPG", result.Focused);
    }

    [Fact]
    public void SelectPath_MissingPath_ReturnsFalseAndKeepsSelection()
    {
        var result = StaRunner.Run(() =>
        {
            var control = new ThumbnailListControl();
            control.SetItems(Paths.Select(p => new ThumbItem(p, System.IO.Path.GetFileName(p))).ToList());
            control.SelectPath(@"C:\Pics\a.png");

            var found = control.SelectPath(@"C:\Pics\nope.png");
            return (Found: found, Selected: control.SelectedPaths.ToList());
        });

        Assert.False(result.Found);
        Assert.Equal(new[] { @"C:\Pics\a.png" }, result.Selected);
    }
}
