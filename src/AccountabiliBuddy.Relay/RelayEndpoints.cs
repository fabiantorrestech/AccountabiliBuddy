using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace AccountabiliBuddy.Relay;

/// <summary>
/// The relay's whole API under <c>/v1/groups/{gid}/</c> (ADR 46): put, list, get and delete the sealed files, and an
/// event stream. Every call carries <c>Authorization: Bearer &lt;token&gt;</c>; the first PUT for a group stores the
/// token's hash, later calls with another token get 403, an unknown group gets 404 (PUT creates it). Bodies are opaque.
/// </summary>
internal sealed class RelayEndpoints
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>The token is HKDF output of 32 bytes in base64url without padding, so exactly 43 characters.</summary>
    private const int TokenLength = 43;

    private readonly GroupStore _store;
    private readonly ChangeBroadcaster _broadcaster;
    private readonly ClientLimits _limits;

    public RelayEndpoints(GroupStore store, ChangeBroadcaster broadcaster, ClientLimits limits)
    {
        _store = store;
        _broadcaster = broadcaster;
        _limits = limits;
    }

    public void Map(WebApplication app)
    {
        app.MapPut("/v1/groups/{gid}/files/{name}", PutFileAsync);
        app.MapGet("/v1/groups/{gid}/files", ListFiles);
        app.MapGet("/v1/groups/{gid}/files/{name}", GetFile);
        app.MapDelete("/v1/groups/{gid}/files/{name}", DeleteFile);
        app.MapGet("/v1/groups/{gid}/events", StreamEventsAsync);
    }

    private async Task<IResult> PutFileAsync(HttpContext context, string gid, string name)
    {
        IResult? rejected = Authenticate(context.Request, gid, name, mustExist: false, out string tokenHash);
        if (rejected is not null)
        {
            return rejected;
        }

        // Refuse before reading the body when the answer doesn't depend on it.
        string address = ClientAddress.Of(context, _limits.BehindProxy);
        GroupAccess access = _store.GetAccess(gid, tokenHash);
        if (access == GroupAccess.NotOwner)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        bool newGroup = access == GroupAccess.NoGroup;
        if (newGroup && !_limits.NewGroups.HasRoom(address, DateTimeOffset.UtcNow))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        byte[]? body = await ReadBodyAsync(context.Request, context.RequestAborted);
        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (body.Length == 0)
        {
            return Results.BadRequest();
        }

        PutResult result;
        try
        {
            result = _store.Put(gid, tokenHash, name, body);
        }
        catch (IOException ex)
        {
            // Only the kind of failure is logged: a message would carry a path.
            Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} storage error {ex.GetType().Name}");
            return Results.StatusCode(IsDiskFull(ex) ? StatusCodes.Status507InsufficientStorage : StatusCodes.Status500InternalServerError);
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} storage error {ex.GetType().Name}");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        switch (result.Status)
        {
            case PutStatus.NotOwner:
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            case PutStatus.TooManyFiles:
            case PutStatus.TooManyGroups:
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            case PutStatus.DiskFull:
                return Results.StatusCode(StatusCodes.Status507InsufficientStorage);
            case PutStatus.Stored:
                if (newGroup)
                {
                    _limits.NewGroups.Record(address, DateTimeOffset.UtcNow);
                }

                _broadcaster.Publish(gid, new ChangeEvent(name, result.ETag));
                break;
        }

        return Results.Json(new { etag = result.ETag, at = result.At.ToString("O") });
    }

    /// <summary>Windows ERROR_HANDLE_DISK_FULL / ERROR_DISK_FULL, or Unix ENOSPC.</summary>
    private static bool IsDiskFull(IOException ex)
    {
        int code = ex.HResult & 0xFFFF;
        return code is 0x27 or 0x70 || ex.HResult == 28;
    }

    private IResult ListFiles(HttpContext context, string gid)
    {
        IResult? rejected = Authenticate(context.Request, gid, name: null, mustExist: true, out _);
        if (rejected is not null)
        {
            return rejected;
        }

        return Results.Json(_store.List(gid).Select(f => new { name = f.Name, etag = f.ETag, size = f.Size, at = f.At.ToString("O") }));
    }

    private IResult GetFile(HttpContext context, string gid, string name)
    {
        IResult? rejected = Authenticate(context.Request, gid, name, mustExist: true, out _);
        if (rejected is not null)
        {
            return rejected;
        }

        (byte[] Bytes, string ETag)? file = _store.TryRead(gid, name);
        if (file is null)
        {
            return Results.NotFound();
        }

        string quoted = $"\"{file.Value.ETag}\"";
        context.Response.Headers.ETag = quoted;
        if (context.Request.Headers.IfNoneMatch.ToString() == quoted)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(file.Value.Bytes, "application/octet-stream");
    }

    private IResult DeleteFile(HttpContext context, string gid, string name)
    {
        IResult? rejected = Authenticate(context.Request, gid, name, mustExist: true, out _);
        if (rejected is not null)
        {
            return rejected;
        }

        if (!_store.Delete(gid, name))
        {
            return Results.NotFound();
        }

        _broadcaster.Publish(gid, new ChangeEvent(name, string.Empty));
        return Results.NoContent();
    }

    /// <summary>Server-sent events: "event: changed" with {name, etag} on each change, and a ":ping" comment every 25 seconds.</summary>
    private async Task<IResult> StreamEventsAsync(HttpContext context, string gid)
    {
        IResult? rejected = Authenticate(context.Request, gid, name: null, mustExist: true, out _);
        if (rejected is not null)
        {
            return rejected;
        }

        using IDisposable? streamLease = _limits.Streams.TryEnter(ClientAddress.Of(context, _limits.BehindProxy));
        if (streamLease is null)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        using ChangeSubscription? subscription = _broadcaster.Subscribe(gid);
        if (subscription is null)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // Without this Kestrel would close a stream whose pings are slower than its minimum data rate. The feature
        // is missing on HTTP/2 (the relay serves HTTP/1.1 only, but a stream must never crash on it).
        IHttpMinResponseDataRateFeature? dataRate = context.Features.Get<IHttpMinResponseDataRateFeature>();
        if (dataRate is not null)
        {
            dataRate.MinDataRate = null;
        }

        CancellationToken closed = context.RequestAborted;
        try
        {
            await WriteAsync(context, ":ok\n\n", closed);
            await PumpAsync(context, subscription, closed);
        }
        catch (OperationCanceledException)
        {
            // The device closed the stream.
        }

        return Results.Empty;
    }

    private static async Task PumpAsync(HttpContext context, ChangeSubscription subscription, CancellationToken closed)
    {
        while (!closed.IsCancellationRequested)
        {
            using var pingTimer = CancellationTokenSource.CreateLinkedTokenSource(closed);
            pingTimer.CancelAfter(RelayLimits.PingInterval);
            try
            {
                if (!await subscription.Reader.WaitToReadAsync(pingTimer.Token))
                {
                    return;
                }

                while (subscription.Reader.TryRead(out ChangeEvent? change))
                {
                    string data = JsonSerializer.Serialize(new { name = change.Name, etag = change.ETag });
                    await WriteAsync(context, $"event: changed\ndata: {data}\n\n", closed);
                }
            }
            catch (OperationCanceledException) when (!closed.IsCancellationRequested)
            {
                await WriteAsync(context, ":ping\n\n", closed);
            }
        }
    }

    private static async Task WriteAsync(HttpContext context, string text, CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync(text, cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Checks the path parts against their strict shapes first (400), then the bearer token (401), then the group:
    /// 404 for a group nobody has written to, 403 for another token's group. Returns null when the call may go on.
    /// </summary>
    private IResult? Authenticate(HttpRequest request, string gid, string? name, bool mustExist, out string tokenHash)
    {
        tokenHash = string.Empty;
        if (!RelayNames.IsGroupId(gid) || (name is not null && !RelayNames.IsFileName(name)))
        {
            return Results.BadRequest();
        }

        string? token = ReadBearerToken(request);
        if (token is null)
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        tokenHash = GroupStore.HashToken(token);
        if (!mustExist)
        {
            return null;
        }

        return _store.GetAccess(gid, tokenHash) switch
        {
            GroupAccess.NoGroup => Results.NotFound(),
            GroupAccess.NotOwner => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => null,
        };
    }

    /// <summary>The token from the Authorization header only (never the URL): exactly 43 characters of the base64url alphabet.</summary>
    private static string? ReadBearerToken(HttpRequest request)
    {
        string header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string token = header[BearerPrefix.Length..].Trim();
        if (token.Length != TokenLength)
        {
            return null;
        }

        foreach (char c in token)
        {
            bool allowed = char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
            if (!allowed)
            {
                return null;
            }
        }

        return token;
    }

    /// <summary>The whole body, or null when it is longer than <see cref="RelayLimits.MaxFileBytes"/>.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > RelayLimits.MaxFileBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > RelayLimits.MaxFileBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
