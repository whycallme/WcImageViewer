using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class PrintDocumentTests
{
    [Fact]
    public void BuildDocument_DistinctImages_WriteDistinctXpsResources()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            string? tempDir = null;
            try
            {
                tempDir = Path.Combine(Path.GetTempPath(), "wc-print-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                // 서로 다른 색·비율의 단색 JPEG 3장을 파일에서 디코딩되도록 저장
                var specs = new (int W, int H, byte R, byte G, byte B)[]
                {
                    (100, 100, 255, 0, 0),
                    (100, 150, 0, 255, 0),
                    (150, 100, 0, 0, 255),
                };

                var paths = new List<string>();
                foreach (var (w, h, r, g, b) in specs)
                {
                    var pixels = new byte[w * h * 4];
                    for (var i = 0; i < w * h; i++)
                    {
                        pixels[i * 4 + 0] = b; // BGRA
                        pixels[i * 4 + 1] = g;
                        pixels[i * 4 + 2] = r;
                        pixels[i * 4 + 3] = 255;
                    }

                    var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
                    var path = Path.Combine(tempDir, $"{paths.Count}.jpg");
                    using (var fs = File.Create(path))
                    {
                        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                        encoder.Frames.Add(BitmapFrame.Create(source));
                        encoder.Save(fs);
                    }
                    paths.Add(path);
                }

                var images = paths.Select(p => ImageLoader.Load(p)).ToList();
                var options = new PrintJobOptions(
                    "p", PrintPaperSize.A4, PrintOrientation.Portrait,
                    PrintFitMode.FitToPage, new PrintMargins(10, 10, 10, 10), 1);

                var document = ImagePrintService.BuildDocument(images, options);

                var xpsPath = Path.Combine(tempDir, "out.xps");
                using (var xd = new System.Windows.Xps.Packaging.XpsDocument(xpsPath, FileAccess.ReadWrite))
                {
                    System.Windows.Xps.Packaging.XpsDocument.CreateXpsDocumentWriter(xd).Write(document);
                }

                int resourceCount;
                using (var zip = ZipFile.OpenRead(xpsPath))
                {
                    resourceCount = zip.Entries.Count(e => e.FullName.StartsWith("Resources/", StringComparison.Ordinal));
                }

                Assert.Equal(3, resourceCount);
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                if (tempDir is not null && Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw new Exception(captured.ToString(), captured);
        }
    }
}
