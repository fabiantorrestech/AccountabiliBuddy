using System.Text.Json;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// Reads BlockSite's blocked sites from its extension storage (values are JSON). The top-level "blockedSites" and
/// "blockedSitesWorkMode" keys and every "blockedSites" list inside other values count; items are site strings or
/// objects with a url/domain/hostname. BlockSite
/// may keep the list in its online account instead, in which case nothing is found here and the user pastes it.
/// </summary>
public static class BlockSiteStorageReader
{
    private static readonly string[] SiteProperties = { "url", "domain", "hostname", "host", "site" };

    public static ImportedBlockList Read(IReadOnlyDictionary<string, string> storage, string name)
    {
        var sites = new List<string>();
        foreach ((string key, string value) in storage)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(value);
                if (key.StartsWith("blockedSites", StringComparison.Ordinal) && document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    // Top-level keys "blockedSites" and "blockedSitesWorkMode" hold the lists themselves.
                    sites.AddRange(document.RootElement.EnumerateArray().Select(SiteOf).OfType<string>());
                }
                else
                {
                    Collect(document.RootElement, sites);
                }
            }
            catch (JsonException)
            {
                // Not JSON (or cut off): skip this value.
            }
        }

        List<string> distinct = sites.Select(site => site.Trim()).Where(site => site.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string[] notes = distinct.Count == 0
            ? new[] { $"{name}: no blocked sites are saved on this PC (BlockSite may keep them in its account). Paste them instead." }
            : new[] { $"{name}: schedules aren't imported; plan your day in the Day plan tab." };
        return new ImportedBlockList(ImportSource.BlockSite, name, distinct, Array.Empty<string>(), Array.Empty<WeekRange>(), null, notes);
    }

    private static void Collect(JsonElement element, List<string> sites)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name == "blockedSites" && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in property.Value.EnumerateArray())
                        {
                            string? site = SiteOf(item);
                            if (site is not null)
                            {
                                sites.Add(site);
                            }
                        }
                    }
                    else
                    {
                        Collect(property.Value, sites);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Collect(item, sites);
                }

                break;
        }
    }

    private static string? SiteOf(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            return item.GetString();
        }

        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (string property in SiteProperties)
            {
                if (item.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }

        return null;
    }
}
