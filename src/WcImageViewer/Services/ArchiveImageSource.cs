using System.Buffers;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

public sealed class ArchiveImageSource : IImageSource
{
    private const int MemoryEntryThreshold = 16 * 1024 * 1024;

    private readonly string _path;
    private readonly ArchiveType _type;
    private readonly ImageEntry[] _entries;
    private readonly EntryState[] _states;
    private readonly Dictionary<string, EntryState> _stateByKey;
    private readonly IArchive? _zipArchive;
    private readonly SemaphoreSlim _zipGate = new(1, 1);
    private readonly CancellationTokenSource _extractionCts = new();
    private readonly Task? _extractionTask;
    private bool _disposed;

    private ArchiveImageSource(
        string path,
        ArchiveType type,
        string displayName,
        ImageEntry[] entries,
        EntryState[] states,
        IArchive? zipArchive,
        bool startExtraction,
        IProgress<ArchiveProgress>? progress)
    {
        _path = path;
        _type = type;
        DisplayName = displayName;
        Id = ArchiveCache.GetDirectory(path);
        _entries = entries;
        _states = states;
        _zipArchive = zipArchive;
        _stateByKey = new Dictionary<string, EntryState>(StringComparer.Ordinal);

        foreach (var state in states)
        {
            _stateByKey[state.Entry.Key] = state;
        }

        if (startExtraction)
        {
            _extractionTask = Task.Run(() => ExtractCore(progress));
        }
    }

    public string DisplayName { get; }

    public string Id { get; }

    public int Count => _entries.Length;

    public ImageEntry GetEntry(int index) => _entries[index];

    public async Task<Stream> OpenEntryAsync(int index, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_zipArchive is not null)
        {
            return await OpenZipEntryAsync(index, cancellationToken);
        }

        var cachePath = await _states[index].Completed.Task.WaitAsync(cancellationToken);
        return new FileStream(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public Func<Stream>? CreateReopenFactory(int index) => null;

    public string? GetFilePath(int index) => null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _extractionCts.Cancel();

        if (_zipArchive is not null)
        {
            try
            {
                _zipGate.Wait(TimeSpan.FromSeconds(2));
            }
            catch (ObjectDisposedException)
            {
            }

            _zipArchive.Dispose();
            _zipGate.Dispose();
        }
    }

    public static Task<ArchiveImageSource> CreateAsync(
        string path,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Create(path, progress, cancellationToken), cancellationToken);
    }

    private static ArchiveImageSource Create(
        string path,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("압축파일을 찾을 수 없습니다.", path);
        }

        IArchive? archive = null;
        try
        {
            archive = ArchiveFactory.OpenArchive(path, ReaderOptions.ForFilePath);

            if (archive.IsEncrypted)
            {
                throw new InvalidDataException("암호가 걸린 압축파일은 아직 지원하지 않습니다.");
            }

            var archiveType = archive.Type;
            var collected = new List<(ImageEntry Entry, string Extension)>();
            long totalBytes = 0;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var key = entry.Key;
                if (string.IsNullOrEmpty(key) || entry.IsDirectory || !ImageFileEnumerator.IsImage(key))
                {
                    continue;
                }

                if (entry.Size < 0 || entry.Size > ArchiveLimits.MaxEntryBytes)
                {
                    continue;
                }

                totalBytes += entry.Size;
                if (totalBytes > ArchiveLimits.MaxTotalBytes)
                {
                    throw new InvalidDataException("압축파일의 이미지 용량이 허용 한도를 초과했습니다.");
                }

                if (collected.Count >= ArchiveLimits.MaxEntries)
                {
                    throw new InvalidDataException("압축파일의 항목 수가 허용 한도를 초과했습니다.");
                }

                collected.Add((
                    new ImageEntry(
                        key,
                        key,
                        entry.Size,
                        entry.LastModifiedTime?.ToUniversalTime()),
                    Path.GetExtension(key)));
            }

            if (collected.Count == 0)
            {
                archive.Dispose();
                archive = null;
                return new ArchiveImageSource(
                    path,
                    archiveType,
                    Path.GetFileName(path),
                    Array.Empty<ImageEntry>(),
                    Array.Empty<EntryState>(),
                    null,
                    false,
                    progress);
            }

            var ordered = collected
                .OrderBy(item => item.Entry.Name, NaturalStringComparer.Instance)
                .ToArray();

            var entries = new ImageEntry[ordered.Length];
            var states = new EntryState[ordered.Length];
            var cacheDirectory = archive.Type == ArchiveType.Zip ? null : ArchiveCache.GetDirectory(path);

            for (var i = 0; i < ordered.Length; i++)
            {
                entries[i] = ordered[i].Entry;
                states[i] = new EntryState(
                    ordered[i].Entry,
                    i,
                    cacheDirectory,
                    ordered[i].Extension);
            }

            if (cacheDirectory is not null && TryCompleteFromCache(cacheDirectory, states))
            {
                archive.Dispose();
                archive = null;
                return new ArchiveImageSource(
                    path,
                    archiveType,
                    Path.GetFileName(path),
                    entries,
                    states,
                    null,
                    false,
                    progress);
            }

            if (cacheDirectory is not null)
            {
                PrepareCacheDirectory(cacheDirectory);
            }

            if (archive.Type == ArchiveType.Zip)
            {
                var zipArchive = archive;
                archive = null;
                return new ArchiveImageSource(
                    path,
                    archiveType,
                    Path.GetFileName(path),
                    entries,
                    states,
                    zipArchive,
                    false,
                    progress);
            }

            archive.Dispose();
            archive = null;
            return new ArchiveImageSource(
                path,
                archiveType,
                Path.GetFileName(path),
                entries,
                states,
                null,
                cacheDirectory is not null,
                progress);
        }
        finally
        {
            archive?.Dispose();
        }
    }

    private static bool TryCompleteFromCache(string cacheDirectory, EntryState[] states)
    {
        if (!File.Exists(Path.Combine(cacheDirectory, ".complete")))
        {
            return false;
        }

        foreach (var state in states)
        {
            if (state.CachePath is null || !File.Exists(state.CachePath))
            {
                return false;
            }
        }

        foreach (var state in states)
        {
            state.Completed.TrySetResult(state.CachePath!);
        }

        return true;
    }

    private static void PrepareCacheDirectory(string cacheDirectory)
    {
        try
        {
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        Directory.CreateDirectory(cacheDirectory);
    }

    private void ExtractCore(IProgress<ArchiveProgress>? progress)
    {
        var completed = 0;

        try
        {
            using var archive = _type == ArchiveType.SevenZip
                ? ArchiveFactory.OpenArchive(_path, ReaderOptions.ForFilePath)
                : null;
            using var reader = archive is not null
                ? archive.ExtractAllEntries()
                : ReaderFactory.OpenReader(_path, ReaderOptions.ForFilePath);

            while (reader.MoveToNextEntry())
            {
                _extractionCts.Token.ThrowIfCancellationRequested();

                var key = reader.Entry.Key;
                if (reader.Entry.IsDirectory ||
                    string.IsNullOrEmpty(key) ||
                    !_stateByKey.TryGetValue(key, out var state) ||
                    state.CachePath is null)
                {
                    continue;
                }

                var temporaryPath = state.CachePath + ".part";

                try
                {
                    using (var output = new FileStream(
                               temporaryPath,
                               FileMode.Create,
                               FileAccess.Write,
                               FileShare.None,
                               128 * 1024,
                               FileOptions.SequentialScan))
                    using (var capped = new CappedWriteStream(output, ArchiveLimits.MaxEntryBytes))
                    {
                        reader.WriteEntryTo(capped);
                        capped.Flush();
                    }

                    File.Move(temporaryPath, state.CachePath, overwrite: true);
                    state.Completed.TrySetResult(state.CachePath);

                    completed++;
                    progress?.Report(new ArchiveProgress(completed, _states.Length, reader.Entry.Key));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    TryDelete(temporaryPath);
                    state.Completed.TrySetException(ex);
                }
            }

            foreach (var state in _states)
            {
                state.Completed.TrySetException(
                    new InvalidDataException("압축파일에서 해당 이미지를 찾을 수 없습니다."));
            }

            if (_states.All(state => state.Completed.Task.Status == TaskStatus.RanToCompletion))
            {
                File.WriteAllText(
                    Path.Combine(Path.GetDirectoryName(_states[0].CachePath!)!, ".complete"),
                    DateTime.UtcNow.ToString("O"));
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var state in _states)
            {
                state.Completed.TrySetCanceled();
            }
        }
        catch (Exception ex)
        {
            foreach (var state in _states)
            {
                state.Completed.TrySetException(ex);
            }
        }
    }

    private async Task<Stream> OpenZipEntryAsync(int index, CancellationToken cancellationToken)
    {
        await _zipGate.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_zipArchive is null)
            {
                throw new InvalidOperationException("압축파일이 열려 있지 않습니다.");
            }

            var key = _entries[index].Key;
            var entry = _zipArchive.Entries.FirstOrDefault(
                item => !item.IsDirectory && string.Equals(item.Key, key, StringComparison.Ordinal));

            if (entry is null)
            {
                throw new InvalidDataException("압축파일에서 해당 이미지를 찾을 수 없습니다.");
            }

            using var entryStream = entry.OpenEntryStream();
            return await CopyToSeekableStreamAsync(entryStream, entry.Size, cancellationToken);
        }
        finally
        {
            _zipGate.Release();
        }
    }

    private static async Task<Stream> CopyToSeekableStreamAsync(
        Stream input,
        long declaredSize,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);

        try
        {
            Stream output;
            if (declaredSize >= 0 && declaredSize <= MemoryEntryThreshold)
            {
                output = new MemoryStream((int)declaredSize);
            }
            else
            {
                var temporaryPath = Path.Combine(
                    Path.GetTempPath(),
                    "wciv_" + Guid.NewGuid().ToString("N") + ".tmp");
                output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            }

            try
            {
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    total += read;
                    if (total > ArchiveLimits.MaxEntryBytes)
                    {
                        throw new InvalidDataException("압축 해제 크기 제한을 초과했습니다.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                output.Position = 0;
                return output;
            }
            catch
            {
                await output.DisposeAsync();
                throw;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class EntryState
    {
        public EntryState(ImageEntry entry, int index, string? cacheDirectory, string extension)
        {
            Entry = entry;
            Index = index;

            if (cacheDirectory is not null)
            {
                CachePath = Path.Combine(
                    cacheDirectory,
                    $"{index:D6}{NormalizeExtension(extension)}");
            }
        }

        public ImageEntry Entry { get; }

        public int Index { get; }

        public string? CachePath { get; }

        public TaskCompletionSource<string> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static string NormalizeExtension(string extension)
        {
            return string.IsNullOrEmpty(extension) ? ".img" : extension.ToLowerInvariant();
        }
    }
}
