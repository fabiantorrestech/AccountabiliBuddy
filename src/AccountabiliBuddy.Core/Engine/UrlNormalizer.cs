using System.Diagnostics.CodeAnalysis;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Puts URLs into a comparable form: lowercase "host/path?query" with no scheme and no leading "www.".
/// Example: "https://www.YouTube.com/shorts/abc" becomes "youtube.com/shorts/abc".
/// </summary>
public static class UrlNormalizer
{
    private const string WwwPrefix = "www.";

    public static string Normalize(string url)
    {
        if (!TryParse(url, out Uri? uri))
        {
            return url.Trim().ToLowerInvariant();
        }

        return (StripWww(uri.Host) + uri.PathAndQuery).ToLowerInvariant();
    }

    /// <summary>Returns the lowercase host without "www.", or null if the URL cannot be parsed.</summary>
    public static string? GetHost(string url)
    {
        if (!TryParse(url, out Uri? uri) || uri.Host.Length == 0)
        {
            return null;
        }

        return StripWww(uri.Host.ToLowerInvariant());
    }

    private static bool TryParse(string url, [NotNullWhen(true)] out Uri? uri)
    {
        string trimmed = url.Trim();
        // Rule patterns are usually written without a scheme ("youtube.com/shorts").
        string withScheme = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        return Uri.TryCreate(withScheme, UriKind.Absolute, out uri);
    }

    private static string StripWww(string host)
    {
        return host.StartsWith(WwwPrefix, StringComparison.OrdinalIgnoreCase)
            ? host[WwwPrefix.Length..]
            : host;
    }
}
