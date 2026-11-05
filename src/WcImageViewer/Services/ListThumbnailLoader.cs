using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WcImageViewer.Services;

public sealed class ListThumbnailLoader
{
    private readonly SemaphoreSlim _gate = new(Math.Max(2, Environment.ProcessorCount / 2));
    private readonly ThumbnailCache _cache = new(800);

    public async Task<BitmapSource?> GetAsync(string path, int size, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        long ticks;
        try
        {
            ticks = new FileInfo(path).LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return null;
        }

        var key = $"{path}|{size}|{ticks}";

        if (_cache.TryGet(key, out var cached))
        {
            return cached;
        }

        if (ct.IsCancellationRequested)
        {
            return null;
        }

        var acquired = false;
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;

            if (_cache.TryGet(key, out cached))
            {
                return cached;
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            BitmapSource? result = null;
            try
            {
                result = await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    using var stream = ImageLoader.OpenFile(path);
                    var thumbnail = ImageLoader.LoadThumbnail(stream, size, ct);
                    if (thumbnail is BitmapSource bitmap && !bitmap.IsFrozen)
                    {
                        bitmap.Freeze();
                    }
                    return thumbnail;
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            if (result is not null)
            {
                _cache.Set(key, result);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }
        }
    }
}