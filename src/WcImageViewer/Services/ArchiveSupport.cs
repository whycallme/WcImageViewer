using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WcImageViewer.Services;

public sealed record ArchiveProgress(int Completed, int Total, string? CurrentEntry);

public static class ArchiveLimits
{
    public const int MaxEntries = 100_000;
    public const long MaxEntryBytes = 512L * 1024 * 1024;
    public const long MaxTotalBytes = 4L * 1024 * 1024 * 1024;
}

public static class ArchiveCache
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WcImageViewer",
        "cache",
        "archives");

    public static string GetDirectory(string archivePath)
    {
        var info = new FileInfo(archivePath);
        var key = $"{archivePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(Root, hash);
    }

    public static void CleanupOld(TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var directory in Directory.EnumerateDirectories(Root))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Prevents a forged archive entry from exhausting disk space.</summary>
internal sealed class CappedWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _written;

    public CappedWriteStream(Stream inner, long limit)
    {
        _inner = inner;
        _limit = limit;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Count(count);
        _inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Count(buffer.Length);
        _inner.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        Count(1);
        _inner.WriteByte(value);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Count(buffer.Length);
        await _inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Count(count);
        return _inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    private void Count(long count)
    {
        _written += count;
        if (_written > _limit)
        {
            throw new InvalidDataException("압축 해제 크기 제한을 초과했습니다.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
