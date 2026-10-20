using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// Answers "where is the PC?" on the local network (ADR 14). A paired device that can't reach the PC at its remembered
/// address (the router gave the PC a new one, or both are on another network) broadcasts one small UDP message with
/// the PC's certificate fingerprint; the PC with that fingerprint answers with its link port, and the device connects
/// to the address the answer came from. Nothing is trusted from this: the device still pins the certificate and signs
/// in with its token. Only private-network senders get an answer; the PC sends nothing on its own.
/// </summary>
public sealed class LinkDiscoveryResponder : IDisposable
{
    private const int MaxRequestBytes = 512;

    private readonly string _fingerprint;
    private readonly int _linkPort;
    private readonly string _pcName;
    private UdpClient? _socket;
    private CancellationTokenSource? _stopping;

    public LinkDiscoveryResponder(string fingerprint, int linkPort, string pcName)
    {
        _fingerprint = fingerprint;
        _linkPort = linkPort;
        _pcName = pcName;
    }

    /// <summary>Listens on UDP <paramref name="port"/>; false when the port is taken (devices then need the remembered address).</summary>
    public bool Start(int port)
    {
        try
        {
            _socket = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        }
        catch (SocketException)
        {
            return false;
        }

        _stopping = new CancellationTokenSource();
        _ = AnswerAsync(_socket, _stopping.Token);
        return true;
    }

    public void Dispose()
    {
        _stopping?.Cancel();
        _socket?.Dispose();
        _socket = null;
    }

    private async Task AnswerAsync(UdpClient socket, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            UdpReceiveResult request;
            try
            {
                request = await socket.ReceiveAsync(stopping).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // E.g. an ICMP "port unreachable" for an earlier answer; keep listening.
                continue;
            }

            if (request.Buffer.Length > MaxRequestBytes || !LinkAddresses.IsPrivate(request.RemoteEndPoint.Address) || !AsksForThisPc(request.Buffer))
            {
                continue;
            }

            byte[] answer = JsonSerializer.SerializeToUtf8Bytes(new { t = "here", p = _linkPort, n = _pcName });
            try
            {
                await socket.SendAsync(answer, request.RemoteEndPoint, stopping).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The device will ask again next time it opens.
            }
        }
    }

    /// <summary>True for {"t":"where","f":"&lt;this PC's fingerprint&gt;"}.</summary>
    private bool AsksForThisPc(byte[] buffer)
    {
        try
        {
            using JsonDocument request = JsonDocument.Parse(Encoding.UTF8.GetString(buffer));
            return request.RootElement.TryGetProperty("t", out JsonElement type) && type.GetString() == "where"
                   && request.RootElement.TryGetProperty("f", out JsonElement fingerprint) && fingerprint.GetString() == _fingerprint;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
