using System.Net;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// What names a relay when a group id and bearer token are derived for it (<see cref="SyncKey.RelayGroupIdFor"/>): its
/// certificate pin when it has one ("pin:" and the 43 characters), else its address as an origin ("origin:https://host"
/// or "origin:https://host:port", host in lower case, an international name as its ASCII form, the port left out when it
/// is 443, an IPv6 address as eight hex groups without zero compression in brackets). The pin is used first because one
/// relay is reached by several addresses (this PC's own relay by 127.0.0.1, a phone by its LAN address) and every device
/// must end up in the same group. The phone spells it the same way (android/shared, <c>RelayIdentity</c>).
/// </summary>
public static class RelayIdentity
{
    private const int DefaultPort = 443;

    /// <param name="address">The relay's https address (see the app's RelayAddress.TryNormalize).</param>
    /// <param name="pin">The relay certificate's pin (43 characters of base64url), or null for a relay with a normal certificate.</param>
    public static string Of(Uri address, string? pin)
    {
        if (pin is not null)
        {
            return "pin:" + pin;
        }

        string host = address.HostNameType == UriHostNameType.IPv6 ? Ipv6Groups(address.DnsSafeHost) : address.IdnHost.ToLowerInvariant();
        return address.Port == DefaultPort ? $"origin:https://{host}" : $"origin:https://{host}:{address.Port}";
    }

    private static string Ipv6Groups(string bareAddress)
    {
        int zone = bareAddress.IndexOf('%');
        byte[] bytes = IPAddress.Parse(zone < 0 ? bareAddress : bareAddress[..zone]).GetAddressBytes();
        var groups = new string[8];
        for (int i = 0; i < 8; i++)
        {
            groups[i] = ((bytes[2 * i] << 8) | bytes[2 * i + 1]).ToString("x");
        }

        return "[" + string.Join(':', groups) + "]";
    }
}
