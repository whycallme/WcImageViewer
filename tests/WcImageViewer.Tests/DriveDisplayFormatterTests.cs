using System.IO;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class DriveDisplayFormatterTests
{
    [Fact]
    public void Format_UsesVolumeLabelWithDriveLetter()
    {
        Assert.Equal("Windows (C:)", DriveDisplayFormatter.Format(@"C:\", "Windows", DriveType.Fixed, true));
    }

    [Fact]
    public void Format_TrimsVolumeLabel()
    {
        Assert.Equal("Data (D:)", DriveDisplayFormatter.Format(@"D:\", "  Data  ", DriveType.Fixed, true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_EmptyLabelFixedDrive_UsesLocalDisk(string? label)
    {
        Assert.Equal("로컬 디스크 (C:)", DriveDisplayFormatter.Format(@"C:\", label, DriveType.Fixed, true));
    }

    [Fact]
    public void Format_EmptyLabelByDriveType_UsesTypeDefaultName()
    {
        Assert.Equal("네트워크 드라이브 (Z:)", DriveDisplayFormatter.Format(@"Z:\", "", DriveType.Network, true));
        Assert.Equal("이동식 디스크 (E:)", DriveDisplayFormatter.Format(@"E:\", null, DriveType.Removable, true));
        Assert.Equal("CD 드라이브 (D:)", DriveDisplayFormatter.Format(@"D:\", null, DriveType.CDRom, true));
        Assert.Equal("드라이브 (X:)", DriveDisplayFormatter.Format(@"X:\", null, DriveType.Unknown, true));
    }

    [Fact]
    public void Format_NotReadyDrive_ReturnsLetterOnlyIgnoringLabel()
    {
        Assert.Equal("(F:)", DriveDisplayFormatter.Format(@"F:\", "Ignored", DriveType.Removable, false));
        Assert.Equal("(F:)", DriveDisplayFormatter.Format(@"F:\", null, DriveType.NoRootDirectory, false));
    }

    [Fact]
    public void Format_LowercaseRoot_ShowsUppercaseLetter()
    {
        Assert.Equal("(D:)", DriveDisplayFormatter.Format(@"d:\", null, DriveType.NoRootDirectory, false));
    }

    [Fact]
    public void FormatDrive_MissingDrive_DoesNotThrowAndShowsLetter()
    {
        var used = new HashSet<char>(DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])));
        var letter = Enumerable.Range('Q', 'Z' - 'Q' + 1).Select(c => (char)c).First(c => !used.Contains(c));

        var text = DriveDisplayFormatter.FormatDrive(new DriveInfo(letter + @":\"));

        Assert.Equal($"({letter}:)", text);
    }
}
