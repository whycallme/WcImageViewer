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
}
