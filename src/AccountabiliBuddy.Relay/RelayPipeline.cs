using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AccountabiliBuddy.Relay;

/// <summary>The middlewares in front of every endpoint: the request log, the per-address rate limit and the per-address cap on requests in flight.</summary>
internal static class RelayPipeline
{
    private const string GroupsPrefix = "/v1/groups/";

    /// <summary>
    /// Answers 429 once an address is over its minute budget. Behind a proxy every connection comes from loopback, so
    /// the address is the last entry of X-Forwarded-For, the one the (trusted, loopback-only) proxy added itself.
    /// </summary>
    public static void UseRateLimit(this WebApplication app, RequestRateLimiter limiter, ClientLimits limits)
    {
        app.Use(async (context, next) =>
        {
            string address = ClientAddress.Of(context, limits.BehindProxy);
            if (!limiter.Allow(address, DateTimeOffset.UtcNow))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "60";
                return;
            }

            await next();
        });
    }

    /// <summary>
    /// Answers 429 when one address already has too many requests in flight (event streams count while they are open).
    /// Behind a proxy this is what limits an address's open connections.
    /// </summary>
    public static void UseInFlightLimit(this WebApplication app, ClientLimits limits)
    {
        app.Use(async (context, next) =>
        {
            using IDisposable? lease = limits.Requests.TryEnter(ClientAddress.Of(context, limits.BehindProxy));
            if (lease is null)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }

            await next();
        });
    }

    /// <summary>
    /// One line per request when it finishes (event streams: when they close): the time, the method, the first 6
    /// characters of the group id and the status. Never the path's file name, a body, a token or an address.
    /// </summary>
    public static void UseRequestLog(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            finally
            {
                Log(context, context.Response.StatusCode);
            }
        });
    }

    private static void Log(HttpContext context, int status)
    {
        string path = context.Request.Path.Value ?? string.Empty;
        string prefix = "------";
        if (path.StartsWith(GroupsPrefix, StringComparison.Ordinal) && path.Length >= GroupsPrefix.Length + RelayNames.GroupIdLength)
        {
            string gid = path.Substring(GroupsPrefix.Length, RelayNames.GroupIdLength);
            if (RelayNames.IsGroupId(gid))
            {
                prefix = gid[..6];
            }
        }

        Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {context.Request.Method,-6} {prefix} {status}");
    }
}
