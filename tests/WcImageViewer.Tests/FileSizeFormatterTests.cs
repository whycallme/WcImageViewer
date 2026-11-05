using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class FileSizeFormatterTests
{
    [Theory]
    [InlineData(-1, "")]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(4000000, "3.81 MB")]
    [InlineData(1073741824, "1 GB")]
    public void FormatReturnsExpectedText(long bytes, string expected)
    {
        Assert.Equal(expected, FileSizeFormatter.Format(bytes));
    }
}
