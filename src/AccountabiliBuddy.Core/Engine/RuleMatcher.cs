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

    // URL patterns, worked out once: a domain rule's host (null = not a URL), an Exact or Prefix rule's normal form.
    private readonly ConcurrentDictionary<string, string?> _patternHosts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _normalizedPatterns = new(StringComparer.Ordinal);

    // A profile's rules are tried one after another against the same snapshot (once a second during focus), so the
    // snapshot's URL is taken apart once, not once per rule.
    private ParsedUrl? _lastUrl;

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
        ParsedUrl parsed = Parse(url);
        if (rule.Kind == MatchKind.Domain)
        {
            return MatchesDomain(parsed.Host, _patternHosts.GetOrAdd(rule.Pattern, UrlNormalizer.GetHost));
        }

        // Exact and Prefix patterns are whole URLs, so normalize them the same way.
        // Contains and Regex patterns are fragments and are used as written.
        string pattern = rule.Kind is MatchKind.Exact or MatchKind.Prefix
            ? _normalizedPatterns.GetOrAdd(rule.Pattern, UrlNormalizer.Normalize)
            : rule.Pattern;

        return MatchesText(rule.Kind, parsed.Normalized, pattern);
    }

    private ParsedUrl Parse(string url)
    {
        ParsedUrl? last = _lastUrl;
        if (last is not null && string.Equals(last.Url, url, StringComparison.Ordinal))
        {
            return last;
        }

        var parsed = new ParsedUrl(url, UrlNormalizer.GetHost(url), UrlNormalizer.Normalize(url));
        _lastUrl = parsed;
        return parsed;
    }

    private static bool MatchesDomain(string? host, string? domain)
    {
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(domain))
        {
            return false;
        }

        // host == domain, or host ends with "." + domain (checked without building that string).
        return host == domain
               || (host.Length > domain.Length
                   && host.EndsWith(domain, StringComparison.Ordinal)
                   && host[host.Length - domain.Length - 1] == '.');
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

    /// <summary>A snapshot's URL with its host (null when it isn't a URL) and normal form.</summary>
    private sealed record ParsedUrl(string Url, string? Host, string Normalized);
}
