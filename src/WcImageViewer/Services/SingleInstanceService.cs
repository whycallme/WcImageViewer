using System.IO;
using System.IO.Pipes;
using System.Text;

namespace WcImageViewer.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\WcImageViewer.SingleInstance";
    private const string PipeName = "WcImageViewer.SingleInstance.Pipe";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;

    private SingleInstanceService(Mutex mutex, bool isPrimary)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public static SingleInstanceService Create()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return new SingleInstanceService(mutex, createdNew);
    }

    public void StartServer(Action<string?> onPath)
    {
        _ = Task.Run(async () =>
        {
            while (!_cancellation.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(_cancellation.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var path = await reader.ReadLineAsync();
                    onPath(string.IsNullOrWhiteSpace(path) ? null : path);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // A client can disconnect or the pipe can be busy; keep listening.
                }
            }
        });
    }

    public static async Task<bool> SendPathAsync(string? path, TimeSpan timeout)
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await client.ConnectAsync((int)timeout.TotalMilliseconds);
            await using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync(path ?? string.Empty);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();

        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
        _cancellation.Dispose();
    }
}
