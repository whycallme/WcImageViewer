using System.Windows.Media.Imaging;

namespace WcImageViewer.Services;

/// <summary>
/// 저장 대상 이미지 형식 식별자. ICO는 WIC 인코더가 없으므로 포함하지 않는다.
/// </summary>
public enum SaveImageFormat
{
    Png,
    Jpeg,
    WebP,
    Bmp,
    Tiff,
    Gif,
}

/// <summary>
/// 형식별 옵션 패널 종류. BMP와 GIF는 <see cref="None"/> 으로 옵션이 없다.
/// </summary>
public enum SaveFormatOptionKind
{
    None,
    JpegQuality,
    WebPQuality,
    TiffCompression,
    PngInterlace,
}

/// <summary>
/// TIFF 저장용 압축 선택. 표시 이름은 "None / LZW / RLE / Zip" 이다.
/// </summary>
public enum TiffSaveCompression
{
    None,
    Lzw,
    Rle,
    Zip,
}

/// <summary>
/// 형식별 저장 옵션 묶음. 기본값은 카드 스펙(품질 95/90, 무손실 끔, 인터레이스 끔, 압축 None)을 따른다.
/// </summary>
public sealed class ImageSaveOptions
{
    public int JpegQuality { get; set; } = 95;
    public int WebPQuality { get; set; } = 90;
    public bool WebPLossless { get; set; }
    public TiffSaveCompression TiffCompression { get; set; } = TiffSaveCompression.None;
    public bool PngInterlace { get; set; }
}

/// <summary>
/// 형식 하나 정의: 표시명, 확장자, 옵션 종류, WIC 인코더 생성 함수.
/// WebP는 WIC 인코더가 없으므로 <see cref="CreateEncoder"/> 가 null 을 돌려주고
/// <see cref="ImageEncoderService"/> 가 Imazen.WebP 로 인코딩한다.
/// </summary>
public sealed class SaveFormatDefinition
{
    public required SaveImageFormat Format { get; init; }

    public required string DisplayName { get; init; }

    public required string Extension { get; init; }

    public required SaveFormatOptionKind OptionKind { get; init; }

    public required bool SupportsAnimation { get; init; }

    public required Func<ImageSaveOptions, BitmapEncoder?> CreateEncoder { get; init; }

    public override string ToString() => $"{DisplayName} ({Extension})";
}

/// <summary>
/// 저장 형식 목록을 한 곳에서 정의한다.
/// </summary>
public static class SaveFormatCatalog
{
    public static IReadOnlyList<SaveFormatDefinition> All { get; } = new List<SaveFormatDefinition>
    {
        new()
        {
            Format = SaveImageFormat.Png,
            DisplayName = "PNG",
            Extension = ".png",
            OptionKind = SaveFormatOptionKind.PngInterlace,
            SupportsAnimation = false,
            CreateEncoder = options => new PngBitmapEncoder
            {
                Interlace = options.PngInterlace
                    ? PngInterlaceOption.On
                    : PngInterlaceOption.Off,
            },
        },
        new()
        {
            Format = SaveImageFormat.Jpeg,
            DisplayName = "JPEG",
            Extension = ".jpg",
            OptionKind = SaveFormatOptionKind.JpegQuality,
            SupportsAnimation = false,
            CreateEncoder = options => new JpegBitmapEncoder
            {
                QualityLevel = Math.Clamp(options.JpegQuality, 1, 100),
            },
        },
        new()
        {
            Format = SaveImageFormat.WebP,
            DisplayName = "WebP",
            Extension = ".webp",
            OptionKind = SaveFormatOptionKind.WebPQuality,
            SupportsAnimation = true,
            CreateEncoder = _ => null,
        },
        new()
        {
            Format = SaveImageFormat.Bmp,
            DisplayName = "BMP",
            Extension = ".bmp",
            OptionKind = SaveFormatOptionKind.None,
            SupportsAnimation = false,
            CreateEncoder = _ => new BmpBitmapEncoder(),
        },
        new()
        {
            Format = SaveImageFormat.Tiff,
            DisplayName = "TIFF",
            Extension = ".tiff",
            OptionKind = SaveFormatOptionKind.TiffCompression,
            SupportsAnimation = false,
            CreateEncoder = options => new TiffBitmapEncoder
            {
                Compression = options.TiffCompression switch
                {
                    TiffSaveCompression.Lzw => TiffCompressOption.Lzw,
                    TiffSaveCompression.Rle => TiffCompressOption.Rle,
                    TiffSaveCompression.Zip => TiffCompressOption.Zip,
                    _ => TiffCompressOption.None,
                },
            },
        },
        new()
        {
            Format = SaveImageFormat.Gif,
            DisplayName = "GIF",
            Extension = ".gif",
            OptionKind = SaveFormatOptionKind.None,
            SupportsAnimation = true,
            CreateEncoder = _ => new GifBitmapEncoder(),
        },
    };

    /// <summary>
    /// 확장자(예: ".jpg")로 형식을 찾는다. 없으면 null.
    /// ".jpg" 와 ".jpeg", ".tif" 와 ".tiff" 를 함께 인정한다.
    /// </summary>
    public static SaveFormatDefinition? ByExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var ext = extension.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        return All.FirstOrDefault(d =>
            string.Equals(d.Extension, ext, StringComparison.OrdinalIgnoreCase)
            || (d.Format == SaveImageFormat.Jpeg && ext is ".jpg" or ".jpeg")
            || (d.Format == SaveImageFormat.Tiff && ext is ".tif" or ".tiff"));
    }

    public static SaveFormatDefinition ByFormat(SaveImageFormat format) =>
        All.First(d => d.Format == format);

    public static SaveFormatDefinition Default => ByFormat(SaveImageFormat.Png);

    public const double LongSaveThresholdMegaPixelFrames = 60;

    public const double PngSaveCostCoefficient = 1.0;
    public const double JpegSaveCostCoefficient = 0.5;
    public const double WebPLossySaveCostCoefficient = 1.0;
    public const double WebPLosslessSaveCostCoefficient = 3.0;
    public const double TiffUncompressedSaveCostCoefficient = 0.6;
    public const double TiffCompressedSaveCostCoefficient = 2.0;
    public const double BmpSaveCostCoefficient = 0.2;
    public const double GifSaveCostCoefficient = 1.5;

    public static double GetSaveCostCoefficient(SaveFormatDefinition format, ImageSaveOptions? options)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format.Format switch
        {
            SaveImageFormat.Png => PngSaveCostCoefficient,
            SaveImageFormat.Jpeg => JpegSaveCostCoefficient,
            SaveImageFormat.WebP => options?.WebPLossless == true
                ? WebPLosslessSaveCostCoefficient
                : WebPLossySaveCostCoefficient,
            SaveImageFormat.Tiff => options?.TiffCompression == TiffSaveCompression.None
                ? TiffUncompressedSaveCostCoefficient
                : TiffCompressedSaveCostCoefficient,
            SaveImageFormat.Bmp => BmpSaveCostCoefficient,
            SaveImageFormat.Gif => GifSaveCostCoefficient,
            _ => PngSaveCostCoefficient,
        };
    }

    public static double EstimateSaveCostMegaPixelFrames(
        long pixelCount,
        int frameCount,
        SaveFormatDefinition format,
        ImageSaveOptions? options = null)
    {
        if (pixelCount < 0 || frameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelCount));
        }

        return pixelCount * (double)frameCount * GetSaveCostCoefficient(format, options) / 1_000_000.0;
    }

    public static double EstimateSaveCostMegaPixelFrames(
        int width,
        int height,
        int frameCount,
        SaveFormatDefinition format,
        ImageSaveOptions? options = null) =>
        EstimateSaveCostMegaPixelFrames((long)width * height, frameCount, format, options);

    public static double EstimateSaveSeconds(
        long pixelCount,
        int frameCount,
        SaveFormatDefinition format,
        ImageSaveOptions? options = null) =>
        EstimateSaveCostMegaPixelFrames(pixelCount, frameCount, format, options);

    public static double EstimateSaveSeconds(
        int width,
        int height,
        int frameCount,
        SaveFormatDefinition format,
        ImageSaveOptions? options = null) =>
        EstimateSaveCostMegaPixelFrames(width, height, frameCount, format, options);

    public static bool ExceedsLongSaveThreshold(
        long pixelCount,
        int frameCount,
        SaveFormatDefinition format,
        ImageSaveOptions? options = null) =>
        EstimateSaveCostMegaPixelFrames(pixelCount, frameCount, format, options) > LongSaveThresholdMegaPixelFrames;

    public static bool ExceedsLongSaveThreshold(
        int width,
        int height,
        int frameCount,
        SaveFormatDefinition format,
        ImageSaveOptions? options = null) =>
        EstimateSaveCostMegaPixelFrames(width, height, frameCount, format, options) > LongSaveThresholdMegaPixelFrames;
}
