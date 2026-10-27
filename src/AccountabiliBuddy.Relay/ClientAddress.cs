using System.Net;
using Microsoft.AspNetCore.Http;

namespace AccountabiliBuddy.Relay;

/// <summary>Who is calling: the connection's address, or behind the loopback-only reverse proxy the last X-Forwarded-For entry (the one the proxy added itself).</summary>
internal static class ClientAddress
{
    public static string Of(HttpContext context, bool behindProxy)
    {
        if (behindProxy && context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            string? last = forwarded.ToString().Split(',').LastOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(last))
            {
                return last;
            }
        }

        return Of(context.Connection.RemoteIpAddress);
    }

    public static string Of(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}
