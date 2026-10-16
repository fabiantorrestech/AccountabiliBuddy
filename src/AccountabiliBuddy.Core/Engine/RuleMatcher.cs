using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Decides whether a single rule matches a snapshot. All comparisons are case-insensitive.</summary>
public sealed class RuleMatcher
{
    private const string ExeSuffix = ".exe";

    // User-written regexes must not be able to hang the watcher.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    // Null value = pattern failed to compile; cached so it is not recompiled on every snapshot.
    private readonly ConcurrentDictionary<string, Regex?> _regexCache = new(StringComparer.Ordinal);

    public bool Matches(Rule rule, ActivitySnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return false;
        }

        return rule.Target switch
        {
            MatchTarget.ProcessName => MatchesText(rule.Kind, StripExe(snapshot.ProcessName), StripExe(rule.Pattern)),
            MatchTarget.WindowTitle => MatchesText(rule.Kind, snapshot.WindowTitle, rule.Pattern),
            MatchTarget.OcrText => snapshot.OcrText is not null && MatchesText(rule.Kind, snapshot.OcrText, rule.Pattern),
            MatchTarget.Url => snapshot.Url is not null && MatchesUrl(rule, snapshot.Url),
            MatchTarget.Channel => snapshot.Channel is not null && MatchesText(rule.Kind, snapshot.Channel, rule.Pattern),
            _ => false,
        };
    }

    private bool MatchesText(MatchKind kind, string input, string pattern)
    {
        return kind switch
        {
            MatchKind.Exact => string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase),
            MatchKind.Contains => input.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            MatchKind.Prefix => input.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            MatchKind.Regex => MatchesRegex(input, pattern),
            // Domain matching only makes sense for URLs.
            MatchKind.Domain => false,
            _ => false,
        };
    }

    private bool MatchesUrl(Rule rule, string url)
    {
        if (rule.Kind == MatchKind.Domain)
        {
            return MatchesDomain(UrlNormalizer.GetHost(url), UrlNormalizer.GetHost(rule.Pattern));
        }

        string normalizedUrl = UrlNormalizer.Normalize(url);

        // Exact and Prefix patterns are whole URLs, so normalize them the same way.
        // Contains and Regex patterns are fragments and are used as written.
        string pattern = rule.Kind is MatchKind.Exact or MatchKind.Prefix
            ? UrlNormalizer.Normalize(rule.Pattern)
            : rule.Pattern;

        return MatchesText(rule.Kind, normalizedUrl, pattern);
    }

    private static bool MatchesDomain(string? host, string? domain)
    {
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(domain))
        {
            return false;
        }

        return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
    }

    private bool MatchesRegex(string input, string pattern)
    {
        Regex? regex = _regexCache.GetOrAdd(pattern, CompileOrNull);
        if (regex is null)
        {
            return false;
        }

        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Regex? CompileOrNull(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string StripExe(string processName)
    {
        return processName.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase)
            ? processName[..^ExeSuffix.Length]
            : processName;
    }
}
