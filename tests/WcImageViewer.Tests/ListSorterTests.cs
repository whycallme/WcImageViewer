using System.IO;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class ListSorterTests
{
    private static WcImageViewer.Services.ListSorter.ImageEntry Entry(string name, long size, DateTime modified) =>
        new(name, name, size, modified, Path.GetExtension(name));

    [Fact]
    public void SortsByNameAscendingNaturally()
    {
        var items = new[]
        {
            Entry("img10.png", 0, DateTime.MinValue),
            Entry("img2.png", 0, DateTime.MinValue)
        };

        var sorted = ListSorter.Sort(items, ListSortKey.Name, false);

        Assert.Equal(new[] { "img2.png", "img10.png" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SortsByNameDescendingNaturally()
    {
        var items = new[]
        {
            Entry("img2.png", 0, DateTime.MinValue),
            Entry("img10.png", 0, DateTime.MinValue)
        };

        var sorted = ListSorter.Sort(items, ListSortKey.Name, true);

        Assert.Equal(new[] { "img10.png", "img2.png" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SortsBySizeAscending()
    {
        var items = new[]
        {
            Entry("b.png", 10, DateTime.MinValue),
            Entry("a.png", 5, DateTime.MinValue)
        };

        var sorted = ListSorter.Sort(items, ListSortKey.Size, false);

        Assert.Equal(new[] { "a.png", "b.png" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SortsBySizeDescending()
    {
        var items = new[]
        {
            Entry("a.png", 5, DateTime.MinValue),
            Entry("b.png", 10, DateTime.MinValue)
        };

        var sorted = ListSorter.Sort(items, ListSortKey.Size, true);

        Assert.Equal(new[] { "b.png", "a.png" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SortsByModifiedAscending()
    {
        var early = new DateTime(2020, 1, 1);
        var late = new DateTime(2021, 1, 1);
        var items = new[]
        {
            Entry("late.png", 0, late),
            Entry("early.png", 0, early)
        };

        var sorted = ListSorter.Sort(items, ListSortKey.Modified, false);

        Assert.Equal(new[] { "early.png", "late.png" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SortsByModifiedDescending()
    {
        var early = new DateTime(2020, 1, 1);
        var late = new DateTime(2021, 1, 1);
        var items = new[]
        {
            Entry("early.png", 0, early),
            Entry("late.png", 0, late)
        };

        var sorted = ListSorter.Sort(items, ListSortKey.Modified, true);

        Assert.Equal(new[] { "late.png", "early.png" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SortsByExtensionAscending()
    {
        var items = new[]
        {
            Entry("a.png", 0, DateTime.MinValue),
            Entry("b.jpg", 0, DateTime.MinValue)
        };

        var sorted = ListSorter.Sort(items, ListSortKey.Extension, false);

        Assert.Equal(new[] { "b.jpg", "a.png" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SortsByExtensionDescending()
    {
        var items = new[]
        {
            Entry("b.jpg", 0, DateTime.MinValue),
            Entry("a.png", 0, DateTime.MinValue)
        };

        var sorted = ListSorter.Sort(items, ListSortKey.Extension, true);

        Assert.Equal(new[] { "a.png", "b.jpg" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void StableOnTies()
    {
        var items = new[]
        {
            Entry("z.png", 5, DateTime.MinValue),
            Entry("a.png", 5, DateTime.MinValue)
        };

        var sorted = ListSorter.Sort(items, ListSortKey.Size, false);

        Assert.Equal(new[] { "z.png", "a.png" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void FromFilePopulatesFields()
    {
        var folder = TestTemp.CreateFolder();
        try
        {
            var path = Path.Combine(folder, "test.png");
            File.WriteAllText(path, "x");

            var entry = ListSorter.FromFile(path);

            Assert.Equal(path, entry.Path);
            Assert.Equal("test.png", entry.Name);
            Assert.Equal(1, entry.Size);
            Assert.Equal(".png", entry.Extension);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}