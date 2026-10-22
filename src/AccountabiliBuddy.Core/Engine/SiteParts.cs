using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// "Only certain parts" of a site in the classify prompt: the part a page is in (its section when the site has
/// sections, ADR 17, else the first step of its path) and the page alone. Patterns are matched like URL rules
/// (<see cref="UrlNormalizer"/>: no scheme, no "www."); they never keep the query or fragment.
/// </summary>
public static class SiteParts
{
    /// <summary>
    /// The part of the site <paramref name="url"/> is in: a subreddit or repo as a Url prefix ("reddit.com/r/dotnet"),
    /// a YouTube channel as a Channel rule, else the first path step ("docs.google.com/document"). Null on a home page.
    /// </summary>
    public static SitePart? PartOf(string url, string? channel)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? address) || UrlNormalizer.GetHost(url) is not string host)
        {
            return null;
        }

        string? section = SiteSections.SectionOf(url, channel);
        if (section is not null && SiteSections.NounFor(host) is (string noun, _))
        {
            return noun == "channel"
                ? new SitePart(MatchTarget.Channel, MatchKind.Exact, section, $"This channel ({section})")
                : new SitePart(MatchTarget.Url, MatchKind.Prefix, $"{host}/{section}", $"This {noun} ({section})");
        }

        string? firstStep = address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstStep is null)
        {
            return null;
        }

        string pattern = $"{host}/{firstStep.ToLowerInvariant()}";
        return new SitePart(MatchTarget.Url, MatchKind.Prefix, pattern, $"This part ({pattern})");
    }

    /// <summary>
    /// The page alone: "host/path" without the query or fragment, as a prefix (so the same page with another query
    /// still matches). A home page is matched exactly, so it never stands for the whole site.
    /// </summary>
    public static SitePart? PageOf(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? address) || UrlNormalizer.GetHost(url) is not string host)
        {
            return null;
        }

        string path = address.AbsolutePath.ToLowerInvariant();
        return path == "/"
            ? new SitePart(MatchTarget.Url, MatchKind.Exact, $"{host}/", "Only this page")
            : new SitePart(MatchTarget.Url, MatchKind.Prefix, host + path.TrimEnd('/'), "Only this page");
    }
}
