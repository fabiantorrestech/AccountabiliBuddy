using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using AccountabiliBuddy.Core.Browser;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Browser;

/// <summary>
/// The app's end of the browser bridge (ADR 5): a named pipe that only the current user can open.
/// Each connected native host (one per browser running the extension) gets its own pipe instance.
/// Messages from the extension:
/// <c>{"type":"hello","browser":"chrome","version":"0.2.0","privateAllowed":true}</c> and
/// <c>{"type":"tabs","tabs":[{"tabId":12,"title":"…","url":"…","channel":null}]}</c> (the active tab of every window),
/// and from extension 0.3.0 <c>{"type":"icon","url":"…","data":"&lt;base64 PNG&gt;"}</c> (a site's icon from the browser's cache).
/// Messages to the extension: <c>{"type":"redirect","tabId":12,"reason":"…"}</c>.
/// Events are raised on background threads.
/// </summary>
public sealed class NamedPipeBrowserSource : IBrowserActivitySource, IDisposable
{
    private static readonly TimeSpan AcceptRetryDelay = TimeSpan.FromSeconds(5);

    private readonly string _pipeName;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private readonly Dictionary<string, PipeConnection> _connections = new(StringComparer.Ordinal);

    private CancellationTokenSource? _stop;

    public NamedPipeBrowserSource(TimeProvider timeProvider)
    {
        string userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current user has no SID.");
        _pipeName = BrowserBridge.PipeName(userSid);
        _timeProvider = timeProvider;
    }

    public event EventHandler? ConnectionsChanged;

    public IReadOnlyList<BrowserConnection> Connections
    {
        get
        {
            lock (_lock)
            {
                return _connections.Values.Select(connection => connection.ToSnapshot()).ToList();
            }
        }
    }

    public void Start()
    {
        if (_stop is not null)
        {
            return;
        }

        _stop = new CancellationTokenSource();
        CancellationToken token = _stop.Token;
        _ = Task.Run(() => AcceptLoopAsync(token), token);
    }

    public void Stop()
    {
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;

        List<PipeConnection> open;
        lock (_lock)
        {
            open = _connections.Values.ToList();
            _connections.Clear();
        }

        foreach (PipeConnection connection in open)
        {
            connection.Pipe.Dispose();
        }
    }

    public void Dispose()
    {
        Stop();
    }

    public async Task RedirectToBlockPageAsync(string tabId, string reason, CancellationToken cancellationToken = default)
    {
        if (!TrySplitTabId(tabId, out string connectionId, out long browserTabId))
        {
            return;
        }

        PipeConnection? connection;
        lock (_lock)
        {
            _connections.TryGetValue(connectionId, out connection);
        }

        if (connection is null)
        {
            return;
        }

        byte[] message = JsonSerializer.SerializeToUtf8Bytes(new { type = "redirect", tabId = browserTabId, reason });
        await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException)
            {
                // Name held by something else (e.g. a stale instance); try again shortly.
                await DelayQuietlyAsync(AcceptRetryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleConnectionAsync(server, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var connection = new PipeConnection(Guid.NewGuid().ToString("N")[..8], pipe, _timeProvider.GetUtcNow());
        lock (_lock)
        {
            _connections[connection.Id] = connection;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[]? message = await NativeMessageFraming.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (Apply(connection, message))
                {
                    ConnectionsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            // Host went away or sent garbage; drop the connection.
        }
        finally
        {
            lock (_lock)
            {
                _connections.Remove(connection.Id);
            }

            await pipe.DisposeAsync().ConfigureAwait(false);
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <returns>True if the connection's state changed.</returns>
    private bool Apply(PipeConnection connection, byte[] message)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type))
            {
                return false;
            }

            if (type.GetString() == "icon")
            {
                RaiseSiteIcon(root);
                return false;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            lock (_lock)
            {
                connection.LastMessageAt = now;

                switch (type.GetString())
                {
                    case "hello":
                        connection.Browser = ParseBrowser(ReadString(root, "browser"));
                        connection.ExtensionVersion = ReadString(root, "version") ?? "?";
                        connection.PrivateWindowsAllowed = root.TryGetProperty("privateAllowed", out JsonElement allowed)
                                                           && allowed.ValueKind is JsonValueKind.True or JsonValueKind.False
                            ? allowed.GetBoolean()
                            : null;
                        return true;

                    case "tabs" when root.TryGetProperty("tabs", out JsonElement tabs) && tabs.ValueKind == JsonValueKind.Array:
                        connection.ActiveTabs = ParseTabs(connection.Id, tabs);
                        return true;

                    default:
                        return false;
                }
            }
        }
    }

    // Site icons are small (32 px); anything much bigger isn't one.
    private const int MaxIconBytes = 64 * 1024;

    public event EventHandler<SiteIcon>? SiteIconReceived;

    private void RaiseSiteIcon(JsonElement root)
    {
        string? url = ReadString(root, "url");
        string? data = ReadString(root, "data");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(data) || data.Length > MaxIconBytes * 4 / 3 + 4)
        {
            return;
        }

        try
        {
            byte[] image = Convert.FromBase64String(data);
            if (image.Length > 0 && image.Length <= MaxIconBytes)
            {
                SiteIconReceived?.Invoke(this, new SiteIcon(url, image));
            }
        }
        catch (FormatException)
        {
            // Not base64; ignore.
        }
    }

    private static IReadOnlyList<BrowserActivity> ParseTabs(string connectionId, JsonElement tabs)
    {
        var result = new List<BrowserActivity>();
        foreach (JsonElement tab in tabs.EnumerateArray())
        {
            if (tab.ValueKind != JsonValueKind.Object
                || !tab.TryGetProperty("tabId", out JsonElement id)
                || !id.TryGetInt64(out long tabId))
            {
                continue;
            }

            result.Add(new BrowserActivity(
                $"{connectionId}:{tabId}",
                ReadString(tab, "title") ?? string.Empty,
                ReadString(tab, "url") ?? string.Empty,
                ReadString(tab, "channel")));
        }

        return result;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static BrowserKind ParseBrowser(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            "chrome" => BrowserKind.Chrome,
            "brave" => BrowserKind.Brave,
            "edge" => BrowserKind.Edge,
            "firefox" => BrowserKind.Firefox,
            _ => BrowserKind.Chromium,
        };
    }

    private static bool TrySplitTabId(string tabId, out string connectionId, out long browserTabId)
    {
        int separator = tabId.IndexOf(':');
        connectionId = separator > 0 ? tabId[..separator] : string.Empty;
        browserTabId = 0;
        return separator > 0 && long.TryParse(tabId[(separator + 1)..], out browserTabId);
    }

    private static async Task DelayQuietlyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>One connected native host. Mutable fields are guarded by the source's lock.</summary>
    private sealed class PipeConnection(string id, NamedPipeServerStream pipe, DateTimeOffset connectedAt)
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public string Id { get; } = id;

        public NamedPipeServerStream Pipe { get; } = pipe;

        public DateTimeOffset ConnectedAt { get; } = connectedAt;

        public DateTimeOffset LastMessageAt { get; set; } = connectedAt;

        public BrowserKind Browser { get; set; } = BrowserKind.Chromium;

        public string ExtensionVersion { get; set; } = "?";

        public bool? PrivateWindowsAllowed { get; set; }

        public IReadOnlyList<BrowserActivity> ActiveTabs { get; set; } = Array.Empty<BrowserActivity>();

        public BrowserConnection ToSnapshot()
        {
            return new BrowserConnection(
                Id, Browser, ExtensionVersion, ConnectedAt, LastMessageAt, ActiveTabs, PrivateWindowsAllowed);
        }

        public async Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await NativeMessageFraming.WriteAsync(Pipe, message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Connection is going away; the read loop cleans it up.
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
