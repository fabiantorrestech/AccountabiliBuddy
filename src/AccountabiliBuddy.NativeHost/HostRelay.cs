using System.IO.Pipes;
using System.Text;
using AccountabiliBuddy.Core.Browser;

namespace AccountabiliBuddy.NativeHost;

/// <summary>
/// Relays framed messages between the browser (stdio) and the app (named pipe) without parsing them.
/// If the app is not running, keeps retrying and tells the extension with a <c>hostStatus</c> message,
/// so the extension can resend its state once the app is back.
/// Exits when the browser closes stdin (extension disconnected or browser closed).
/// </summary>
public sealed class HostRelay
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly Stream _fromBrowser;
    private readonly Stream _toBrowser;
    private readonly string _pipeName;

    private readonly SemaphoreSlim _browserWriteLock = new(1, 1);
    private readonly SemaphoreSlim _pipeWriteLock = new(1, 1);

    private NamedPipeClientStream? _pipe;

    public HostRelay(Stream fromBrowser, Stream toBrowser, string pipeName)
    {
        _fromBrowser = fromBrowser;
        _toBrowser = toBrowser;
        _pipeName = pipeName;
    }

    public async Task RunAsync()
    {
        using var stop = new CancellationTokenSource();
        Task appLink = KeepAppConnectedAsync(stop.Token);

        try
        {
            await RelayBrowserToAppAsync(stop.Token);
        }
        finally
        {
            await stop.CancelAsync();
            _pipe?.Dispose();
            try
            {
                await appLink;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task RelayBrowserToAppAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            byte[]? message;
            try
            {
                message = await NativeMessageFraming.ReadAsync(_fromBrowser, cancellationToken);
            }
            catch (InvalidDataException)
            {
                return; // Corrupt stream from the browser: nothing sensible left to do.
            }

            if (message is null)
            {
                return; // Browser closed the connection.
            }

            NamedPipeClientStream? pipe = _pipe;
            if (pipe is null)
            {
                continue; // App not running; the extension resends its state when we report it is back.
            }

            await _pipeWriteLock.WaitAsync(cancellationToken);
            try
            {
                await NativeMessageFraming.WriteAsync(pipe, message, cancellationToken);
            }
            catch (IOException)
            {
                // The read loop notices the broken pipe and reconnects.
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _pipeWriteLock.Release();
            }
        }
    }

    private async Task KeepAppConnectedAsync(CancellationToken cancellationToken)
    {
        bool? lastReported = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeClientStream(
                ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(ConnectTimeout, cancellationToken);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                await pipe.DisposeAsync();
                if (lastReported != false)
                {
                    await SendStatusAsync(appConnected: false, cancellationToken);
                    lastReported = false;
                }

                await Task.Delay(RetryDelay, cancellationToken);
                continue;
            }

            _pipe = pipe;
            await SendStatusAsync(appConnected: true, cancellationToken);
            lastReported = true;

            await RelayAppToBrowserAsync(pipe, cancellationToken);

            _pipe = null;
            await pipe.DisposeAsync();
            await SendStatusAsync(appConnected: false, cancellationToken);
            lastReported = false;
            await Task.Delay(RetryDelay, cancellationToken);
        }
    }

    private async Task RelayAppToBrowserAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[]? message;
            try
            {
                message = await NativeMessageFraming.ReadAsync(pipe, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
            {
                return;
            }

            if (message is null)
            {
                return; // App closed the pipe.
            }

            await WriteToBrowserAsync(message, cancellationToken);
        }
    }

    private Task SendStatusAsync(bool appConnected, CancellationToken cancellationToken)
    {
        string json = $$"""{"type":"hostStatus","appConnected":{{(appConnected ? "true" : "false")}}}""";
        return WriteToBrowserAsync(Encoding.UTF8.GetBytes(json), cancellationToken);
    }

    private async Task WriteToBrowserAsync(byte[] message, CancellationToken cancellationToken)
    {
        await _browserWriteLock.WaitAsync(cancellationToken);
        try
        {
            await NativeMessageFraming.WriteAsync(_toBrowser, message, cancellationToken);
        }
        finally
        {
            _browserWriteLock.Release();
        }
    }
}
