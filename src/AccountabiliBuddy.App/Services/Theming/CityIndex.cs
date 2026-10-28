using System.Globalization;
using System.Text;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>
/// Searches the bundled city list in memory (ADR 49). The list loads (off the UI thread) on the first search and
/// is let go with <see cref="Release"/> when the picker closes. Matches the start of the name first, then anywhere in it, ignoring case and
/// accents; the list is largest cities first, so bigger cities come first within each group.
/// </summary>
public sealed class CityIndex
{
    public const int MaxResults = 20;

    private static readonly Uri ListUri = new("avares://AccountabiliBuddy.App/Assets/Data/cities.tsv");

    private Task<List<Entry>>? _loading;

    /// <summary>Finds cities for <paramref name="query"/>; the first call reads and folds the list on a worker thread.</summary>
    public async Task<IReadOnlyList<City>> SearchAsync(string query)
    {
        string wanted = Fold(query.Trim());
        if (wanted.Length == 0)
        {
            return [];
        }

        List<Entry> entries = await (_loading ??= Task.Run(Load));
        return Match(entries, wanted);
    }

    private static List<City> Match(List<Entry> entries, string wanted)
    {
        var results = new List<City>(MaxResults);
        foreach (Entry entry in entries)
        {
            if (entry.Folded.StartsWith(wanted, StringComparison.Ordinal))
            {
                results.Add(entry.City);
                if (results.Count == MaxResults)
                {
                    return results;
                }
            }
        }

        foreach (Entry entry in entries)
        {
            if (!entry.Folded.StartsWith(wanted, StringComparison.Ordinal) && entry.Folded.Contains(wanted, StringComparison.Ordinal))
            {
                results.Add(entry.City);
                if (results.Count == MaxResults)
                {
                    break;
                }
            }
        }

        return results;
    }

    /// <summary>Lets the list go; the next search loads it again.</summary>
    public void Release() => _loading = null;

    private static List<Entry> Load()
    {
        var entries = new List<Entry>(35000);
        using Stream stream = AssetLoader.Open(ListUri);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is string line)
        {
            // geonameid, name, admin1 code, country code, latitude, longitude
            string[] parts = line.Split('\t');
            if (parts.Length < 6
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                || !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude)
                || !double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude))
            {
                continue;
            }

            entries.Add(new Entry(new City(id, parts[1], parts[2], parts[3], new GeoPoint(latitude, longitude)), Fold(parts[1])));
        }

        return entries;
    }

    // Lower case with accents taken off: "Zürich" is found by "zurich".
    private static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private readonly record struct Entry(City City, string Folded);
}
