namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Recognizes Cold Turkey's block page in a browser tab and, when its address says so, which site it blocked.
/// Chromium extension ids come from Cold Turkey's own native messaging manifests. Firefox gives every add-on a
/// random per-install address, so there the page is recognized by its title instead.
/// The block page's address format is not documented; when no blocked site can be read from it, the page is
/// still recognized but the site is unknown.
/// </summary>
public static class ColdTurkeyBlockPage
{
    private static readonly string[] ChromiumExtensionIds =
    {
        "pganeibhckoanndahmnfggfoeofncnii", // Chrome Web Store
        "efnolkfmdbinpkbnfigocgfglnhahldj",
        "jfphahkinplobmabmgjmjgflbhjjddeb", // Edge Add-ons
    };

    public static bool IsBlockPage(string url, string title)
    {
        if (url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
        {
            return ChromiumExtensionIds.Any(id => url.Contains($"://{id}/", StringComparison.OrdinalIgnoreCase));
        }

        return url.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase)
            && title.Contains("Cold Turkey", StringComparison.OrdinalIgnoreCase);
    }

    /// <returns>The blocked site's host (e.g. "reddit.com"), or null if the address does not include it.</returns>
    public static string? TryGetBlockedSite(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        // Look for any query or fragment value that is itself a web address or a bare domain.
        string parameters = (uri.Query.TrimStart('?') + "&" + uri.Fragment.TrimStart('#')).Trim('&');
        foreach (string pair in parameters.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string value = Uri.UnescapeDataString(equals >= 0 ? pair[(equals + 1)..] : pair);

            string? host = UrlNormalizer.GetHost(value);
            if (host is not null && host.Contains('.') && !value.Contains(' '))
            {
                return host;
            }
        }

        return null;
    }
}
