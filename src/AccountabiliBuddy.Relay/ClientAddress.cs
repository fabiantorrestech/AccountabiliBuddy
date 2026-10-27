using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace AccountabiliBuddy.Relay;

/// <summary>
/// Who is calling, as the key of the per-client limits: an IPv4 address as it is, an IPv6 address as its /64 (one home
/// or server connection gets a whole /64, so keying on the full address would give an attacker 2^64 "clients").
/// Behind the reverse proxy the client is the last X-Forwarded-For entry (the one the proxy added itself), and that
/// header is read only on a connection that comes from the loopback address, i.e. from the proxy.
/// </summary>
internal static class ClientAddress
{
    public static string Of(HttpContext context, bool behindProxy)
    {
        IPAddress? connection = context.Connection.RemoteIpAddress;
        if (behindProxy && IsLoopback(connection) && context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            string? last = forwarded.ToString().Split(',').LastOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(last) && IPAddress.TryParse(last, out IPAddress? parsed))
            {
                return Of(parsed);
            }
        }

        return Of(connection);
    }

    public static string Of(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        IPAddress plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (plain.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return plain.ToString();
        }

        byte[] prefix = plain.GetAddressBytes()[..8];
        return Convert.ToHexStringLower(prefix) + "/64";
    }

    private static bool IsLoopback(IPAddress? address)
    {
        return address is not null && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }
}
