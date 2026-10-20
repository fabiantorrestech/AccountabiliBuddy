using System.Text.RegularExpressions;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The part of a site that History breaks its time down by (ADR 17), for a fixed list of sites only: a Reddit
/// subreddit ("r/programminghelp"), a YouTube channel (as the browser extension reports it) or a GitHub repository
/// ("owner/repo"). Other sites, and pages of these outside any section (Reddit's home feed, GitHub's settings), have
/// none. Adding a site needs the user's OK (ADR 17).
/// </summary>
public static partial class SiteSections
{
    // GitHub's own pages, whose first path part is not an account.
    private static readonly HashSet<string> GitHubPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "account", "apps", "codespaces", "collections", "contact", "copilot", "customer-stories", "dashboard",
        "enterprise", "events", "explore", "features", "issues", "login", "logout", "marketplace", "new",
        "notifications", "organizations", "orgs", "pricing", "pulls", "search", "security", "settings", "signup",
        "site", "sponsors", "stars", "team", "topics", "trending", "watching",
    };

    /// <param name="url">The page's full address (stored encrypted; only the section is kept readable).</param>
    /// <param name="channel">The channel the browser extension reported for the page, if any.</param>
    /// <returns>E.g. "r/MouseReview", "dotnet/runtime" or "Linus Tech Tips"; null when the page has no section.</returns>
    public static string? SectionOf(string? url, string? channel)
    {
        if (url is null || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? address))
        {
            return null;
        }

        string host = address.Host.ToLowerInvariant();
        string[] path = address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (IsOrUnder(host, "reddit.com"))
        {
            return Subreddit(path);
        }

        if (host is "github.com" or "www.github.com")
        {
            return Repository(path);
        }

        if (IsOrUnder(host, "youtube.com"))
        {
            return string.IsNullOrWhiteSpace(channel) ? null : channel.Trim();
        }

        return null;
    }

    /// <summary>What a section of this site is called, for "+3 more subreddits"; null for a site without sections.</summary>
    /// <param name="site">A domain as History stores it (no "www.").</param>
    public static (string One, string Many)? NounFor(string site)
    {
        string host = site.ToLowerInvariant();
        return IsOrUnder(host, "reddit.com") ? ("subreddit", "subreddits")
            : host == "github.com" ? ("repo", "repos")
            : IsOrUnder(host, "youtube.com") ? ("channel", "channels")
            : null;
    }

    private static string? Subreddit(string[] path)
    {
        return path.Length >= 2 && path[0].Equals("r", StringComparison.OrdinalIgnoreCase) && SubredditName().IsMatch(path[1])
            ? $"r/{path[1]}"
            : null;
    }

    private static string? Repository(string[] path)
    {
        if (path.Length < 2 || GitHubPages.Contains(path[0]) || !GitHubAccount().IsMatch(path[0]))
        {
            return null;
        }

        string repository = path[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? path[1][..^4] : path[1];
        return GitHubRepository().IsMatch(repository) ? $"{path[0]}/{repository}" : null;
    }

    private static bool IsOrUnder(string host, string site)
    {
        return host == site || host.EndsWith("." + site, StringComparison.Ordinal);
    }

    // Subreddit names are 2-21 letters, digits or underscores; "a+b" (several at once) isn't one subreddit.
    [GeneratedRegex("^[A-Za-z0-9_]{2,21}$")]
    private static partial Regex SubredditName();

    [GeneratedRegex("^[A-Za-z0-9-]{1,39}$")]
    private static partial Regex GitHubAccount();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex GitHubRepository();
}
