using System.Diagnostics.CodeAnalysis;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Checks and tidies what the user types for the own relay (ADR 46): the address (HTTPS only) and the certificate pin.
/// A pin that is typed but wrong is refused, never dropped: dropping it would silently turn pinning off.
/// </summary>
public static class RelayAddress
{
    /// <summary>The SHA-256 fingerprint in base64url without padding is 43 characters.</summary>
    private const int PinLength = 43;

    /// <summary>
    /// An address like "192.168.1.20:8743" or "https://relay.example.com" as an https address ending in "/". An "http://"
    /// address, one with a path (the relay serves from the root), a user name or password, a query or a fragment is refused.
    /// </summary>
    public static bool TryNormalize(string? text, [NotNullWhen(true)] out Uri? address)
    {
        address = null;
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(parsed.Host)
            || parsed.AbsolutePath != "/"
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        address = new Uri(parsed.GetLeftPart(UriPartial.Authority) + "/");
        return true;
    }

    /// <summary>
    /// The pin as the relay prints it (43 characters of base64url). Empty or blank is fine and means no pin (null). The
    /// standard base64 alphabet and padding are accepted and turned into base64url.
    /// </summary>
    /// <returns>False when something is typed but isn't a fingerprint.</returns>
    public static bool TryNormalizePin(string? text, out string? pin)
    {
        pin = null;
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        string url = trimmed.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (url.Length != PinLength || !url.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return false;
        }

        pin = url;
        return true;
    }

    /// <summary>
    /// The relay's admission key as it prints it (43 characters of base64url). Empty or blank is fine and means none (null).
    /// </summary>
    /// <returns>False when something is typed but isn't an admission key.</returns>
    public static bool TryNormalizeAdmissionKey(string? text, out string? key)
    {
        key = null;
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (trimmed.Length != PinLength || !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return false;
        }

        key = trimmed;
        return true;
    }

    /// <summary>The address as shown and put in a join link: no trailing "/".</summary>
    public static string Format(Uri address)
    {
        return address.GetLeftPart(UriPartial.Authority);
    }
}
