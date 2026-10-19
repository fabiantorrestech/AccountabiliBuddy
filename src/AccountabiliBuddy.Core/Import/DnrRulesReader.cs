using System.Text.Json;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// Reads the sites an extension blocks from the browser's copy of its declarativeNetRequest rules
/// (…\User Data\&lt;profile&gt;\DNR Extension Rules\&lt;extension id&gt;\rules.json), the standard Chromium format
/// AppBlock uses. Block and redirect rules count; "||example.com^" filters become "example.com".
/// </summary>
public static class DnrRulesReader
{
    public static ImportedBlockList Read(string rulesJson, ImportSource source, string name)
    {
        var sites = new List<string>();
        var notes = new List<string>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(rulesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException($"{name}: the rules file isn't a list.");
            }

            foreach (JsonElement rule in document.RootElement.EnumerateArray())
            {
                if (!IsBlocking(rule) || !rule.TryGetProperty("condition", out JsonElement condition))
                {
                    continue;
                }

                if (condition.TryGetProperty("urlFilter", out JsonElement filter) && filter.ValueKind == JsonValueKind.String)
                {
                    sites.Add(SiteOf(filter.GetString()!));
                }

                if (condition.TryGetProperty("requestDomains", out JsonElement domains) && domains.ValueKind == JsonValueKind.Array)
                {
                    sites.AddRange(domains.EnumerateArray()
                        .Where(domain => domain.ValueKind == JsonValueKind.String)
                        .Select(domain => domain.GetString()!));
                }

                if (condition.TryGetProperty("regexFilter", out _))
                {
                    notes.Add($"{name}: a pattern rule can't be imported.");
                }
            }
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{name}: the rules file isn't valid JSON.", ex);
        }

        List<string> distinct = sites.Where(site => site.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new ImportedBlockList(source, name, distinct, Array.Empty<string>(), Array.Empty<WeekRange>(), null, notes);
    }

    private static bool IsBlocking(JsonElement rule)
    {
        return rule.TryGetProperty("action", out JsonElement action)
            && action.TryGetProperty("type", out JsonElement type)
            && type.GetString() is "block" or "redirect";
    }

    /// <summary>"||tiktok.com^" → "tiktok.com"; "|https://x.com/path" → "x.com/path"; wildcards are kept.</summary>
    private static string SiteOf(string filter)
    {
        string site = filter.Trim().TrimStart('|').TrimEnd('^', '|');
        int scheme = site.IndexOf("://", StringComparison.Ordinal);
        return scheme >= 0 ? site[(scheme + 3)..] : site;
    }
}
