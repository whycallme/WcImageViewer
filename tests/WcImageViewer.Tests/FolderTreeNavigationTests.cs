using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class FolderTreeNavigationTests
{
    [Fact]
    public void BuildChain_ReturnsRootThenEachAncestorDownToTarget()
    {
        var chain = FolderTreeNavigation.BuildChain(@"C:\", @"C:\Users\me\Pictures");

        Assert.Equal(
            new[] { @"C:\", @"C:\Users", @"C:\Users\me", @"C:\Users\me\Pictures" },
            chain);
    }

    [Fact]
    public void BuildChain_TargetEqualToRoot_ReturnsOnlyRoot()
    {
        var chain = FolderTreeNavigation.BuildChain(@"C:\", @"C:\");

        Assert.Equal(new[] { @"C:\" }, chain);
    }

    [Fact]
    public void BuildChain_TargetOutsideRoot_ReturnsEmpty()
    {
        Assert.Empty(FolderTreeNavigation.BuildChain(@"C:\", @"D:\docs"));
        Assert.Empty(FolderTreeNavigation.BuildChain(@"C:\Users", @"C:\Users2\x"));
    }

    [Fact]
    public void BuildChain_IgnoresCaseAndTrailingSeparator()
    {
        var chain = FolderTreeNavigation.BuildChain(@"c:\", @"c:\USERS\Me\");

        Assert.Equal(3, chain.Count);
        Assert.Equal(@"c:\USERS\Me", chain[2]);
    }

    [Fact]
    public void BuildChain_NormalizesDotSegments()
    {
        var chain = FolderTreeNavigation.BuildChain(@"C:\", @"C:\a\..\b");

        Assert.Equal(new[] { @"C:\", @"C:\b" }, chain);
    }

    [Fact]
    public void IndexOfChild_MatchesChildRegardlessOfCase()
    {
        var children = new List<string> { @"C:\Users\Alpha", @"C:\Users\Beta", @"C:\Users\Gamma" };

        Assert.Equal(1, FolderTreeNavigation.IndexOfChild(children, @"c:\users\BETA"));
    }

    [Fact]
    public void IndexOfChild_MissingChild_ReturnsMinusOne()
    {
        var children = new List<string> { @"C:\Users\Alpha" };

        Assert.Equal(-1, FolderTreeNavigation.IndexOfChild(children, @"C:\Users\Zeta"));
        Assert.Equal(-1, FolderTreeNavigation.IndexOfChild(children, string.Empty));
    }
}
