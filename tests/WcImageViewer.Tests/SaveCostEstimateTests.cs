using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class SaveCostEstimateTests
{
    [Fact]
    public void SmallImageBelowThreshold()
    {
        var format = SaveFormatCatalog.ByFormat(SaveImageFormat.Png);
        var cost = SaveFormatCatalog.EstimateSaveCostMegaPixelFrames(1000, 1000, 1, format, new ImageSaveOptions());
        Assert.True(cost < SaveFormatCatalog.LongSaveThresholdMegaPixelFrames);
        Assert.False(SaveFormatCatalog.ExceedsLongSaveThreshold(1000, 1000, 1, format, new ImageSaveOptions()));
    }

    [Fact]
    public void LargeImageExceedsThreshold()
    {
        var format = SaveFormatCatalog.ByFormat(SaveImageFormat.Png);
        var cost = SaveFormatCatalog.EstimateSaveCostMegaPixelFrames(10000, 10000, 1, format, new ImageSaveOptions());
        Assert.True(cost > SaveFormatCatalog.LongSaveThresholdMegaPixelFrames);
        Assert.True(SaveFormatCatalog.ExceedsLongSaveThreshold(10000, 10000, 1, format, new ImageSaveOptions()));
    }

    [Fact]
    public void LosslessWebPPaysMoreThanLossy()
    {
        var format = SaveFormatCatalog.ByFormat(SaveImageFormat.WebP);
        var lossy = SaveFormatCatalog.EstimateSaveCostMegaPixelFrames(2000, 2000, 1, format, new ImageSaveOptions { WebPLossless = false });
        var lossless = SaveFormatCatalog.EstimateSaveCostMegaPixelFrames(2000, 2000, 1, format, new ImageSaveOptions { WebPLossless = true });
        Assert.True(lossless > lossy);
    }

    [Fact]
    public void TiffCompressedPaysMoreThanUncompressed()
    {
        var format = SaveFormatCatalog.ByFormat(SaveImageFormat.Tiff);
        var uncompressed = SaveFormatCatalog.EstimateSaveCostMegaPixelFrames(2000, 2000, 1, format, new ImageSaveOptions { TiffCompression = TiffSaveCompression.None });
        var compressed = SaveFormatCatalog.EstimateSaveCostMegaPixelFrames(2000, 2000, 1, format, new ImageSaveOptions { TiffCompression = TiffSaveCompression.Lzw });
        Assert.True(compressed > uncompressed);
    }
}
