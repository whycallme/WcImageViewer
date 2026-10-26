using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WcImageViewer.Tests;

internal static class StaRunner
{
    public static T Run<T>(Func<T> action)
    {
        T result = default!;
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return result;
    }

    public static void Run(Action action)
    {
        Run<object?>(() =>
        {
            action();
            return null;
        });
    }
}

internal static class TestTemp
{
    public static string CreateFolder(string prefix = "wc_image_viewer_tests")
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

internal static class TestImages
{
    public static void CreatePng(string path, int width, int height, byte value = 0)
    {
        StaRunner.Run(() =>
        {
            var pixels = new byte[width * height * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = value;
            }

            var source = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                width * 4);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(path);
            encoder.Save(stream);
        });
    }

    public static void CreatePatternPng(string path, int width, int height)
    {
        StaRunner.Run(() =>
        {
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * 4;
                    pixels[offset] = (byte)(x * 17 + 3);
                    pixels[offset + 1] = (byte)(y * 29 + 5);
                    pixels[offset + 2] = (byte)((x + y) * 11 + 7);
                    pixels[offset + 3] = 255;
                }
            }

            var source = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                width * 4);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(path);
            encoder.Save(stream);
        });
    }

    public static byte[] GetPixels(BitmapSource source)
    {
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
