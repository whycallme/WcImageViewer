using System.IO;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class NaturalStringComparerTests
{
    [Theory]
    [InlineData("img2.jpg", "img10.jpg")]
    [InlineData("1.png", "2.png")]
    [InlineData("a1b2.png", "a1b10.png")]
    public void SortsNumbersNaturally(string first, string second)
    {
        Assert.True(NaturalStringComparer.Instance.Compare(first, second) < 0);
        Assert.True(NaturalStringComparer.Instance.Compare(second, first) > 0);
    }

    [Fact]
    public void EqualStringsCompareEqual()
    {
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("same.jpg", "same.jpg"));
    }
}

public class ImageFileEnumeratorTests
{
    [Fact]
    public void FiltersImagesAndSortsNaturally()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wc_image_viewer_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            foreach (var name in new[] { "10.png", "2.png", "1.jpg", "note.txt", "archive.zip" })
            {
                File.WriteAllBytes(Path.Combine(folder, name), Array.Empty<byte>());
            }

            var files = ImageFileEnumerator.Enumerate(folder);

            Assert.Equal(new[] { "1.jpg", "2.png", "10.png" }, files.Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("photo.JPG", true)]
    [InlineData("photo.jpeg", true)]
    [InlineData("photo.webp", false)]
    [InlineData("photo.txt", false)]
    public void RecognizesSupportedExtensions(string name, bool expected)
    {
        Assert.Equal(expected, ImageFileEnumerator.IsImage(name));
    }
}
