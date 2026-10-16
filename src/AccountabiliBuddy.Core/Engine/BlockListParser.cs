using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Turns a pasted list (one entry per line, as copied from Cold Turkey, LeechBlock, BlockSite, ...) into rules.
/// <list type="bullet">
/// <item><c>example.com</c>, <c>*.example.com</c>, <c>https://www.example.com</c> → URL domain rule</item>
/// <item><c>example.com/path</c> → URL prefix rule</item>
/// <item><c>*reddit*/r/all*</c> (wildcard inside) → URL regex rule</item>
/// <item><c>Discord.exe</c> or <c>C:\Apps\Discord.exe</c> → process rule</item>
/// </list>
/// Blank lines and lines starting with <c>#</c> or <c>//</c> are ignored; duplicates are dropped.
/// </summary>
public static class BlockListParser
{
    private static readonly string[] Schemes = { "https://", "http://" };

    public static BlockListParseResult Parse(string text, RuleVerdict verdict, string note)
    {
        var rules = new List<Rule>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            (MatchTarget Target, MatchKind Kind, string Pattern)? parsed = ParseLine(line);
            if (parsed is not { } entry || entry.Pattern.Length == 0)
            {
                skipped.Add(line);
                continue;
            }

            if (seen.Add($"{entry.Target}|{entry.Kind}|{entry.Pattern}"))
            {
                rules.Add(new Rule(Guid.NewGuid(), entry.Target, entry.Kind, entry.Pattern, verdict, note));
            }
        }

        return new BlockListParseResult(rules, skipped);
    }

    private static (MatchTarget Target, MatchKind Kind, string Pattern)? ParseLine(string line)
    {
        if (line.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            // Full paths ("C:\Apps\Discord.exe") and bare names both become the process name.
            string fileName = line.Replace('/', '\\').Split('\\')[^1];
            return (MatchTarget.ProcessName, MatchKind.Exact, fileName[..^".exe".Length]);
        }

        string entry = StripScheme(line);
        if (entry.StartsWith("*.", StringComparison.Ordinal))
        {
            entry = entry[2..];
        }

        if (entry.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            entry = entry[4..];
        }

        entry = entry.TrimEnd('/').ToLowerInvariant();
        if (entry.Length == 0 || entry.Contains(' '))
        {
            return null;
        }

        if (entry.Contains('*'))
        {
            // Wildcards anywhere else: match against the normalized URL ("host/path?query").
            string regex = "^" + Regex.Escape(entry).Replace("\\*", ".*", StringComparison.Ordinal);
            return (MatchTarget.Url, MatchKind.Regex, regex);
        }

        return entry.Contains('/')
            ? (MatchTarget.Url, MatchKind.Prefix, entry)
            : (MatchTarget.Url, MatchKind.Domain, entry);
    }

    private static string StripScheme(string line)
    {
        foreach (string scheme in Schemes)
        {
            if (line.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                return line[scheme.Length..];
            }
        }

        return line;
    }
}
