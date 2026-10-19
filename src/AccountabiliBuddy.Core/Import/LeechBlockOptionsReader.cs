using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// Reads LeechBlock NG block sets from its exported options file (LeechBlockOptions-*.txt with key=value lines, or
/// the .json export) or from its extension storage. Per set n: setName{n}, sites{n} (separated by spaces;
/// "+" = allow, "~" = keyword, ">" = referrer, "#" = comment), times{n} ("0900-1700,1800-2000"), days{n}
/// (a bitmask with Sunday as bit 0 in the .txt file, an array of 7 booleans from Sunday in JSON).
/// </summary>
public static class LeechBlockOptionsReader
{
    private const int MaxSets = 30;
    private static readonly Regex OptionLine = new(@"^(\w+)=(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex TimePeriod = new(@"^(\d{4})-(\d{4})$", RegexOptions.CultureInvariant);

    /// <summary>Reads an exported options file (.txt or .json).</summary>
    public static IReadOnlyList<ImportedBlockList> ReadExportFile(string text)
    {
        string trimmed = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(trimmed);
                return ReadOptions(document.RootElement.EnumerateObject()
                    .ToDictionary(property => property.Name, property => property.Value.GetRawText()), valuesAreJson: true);
            }
            catch (JsonException ex)
            {
                throw new FormatException("The LeechBlock file isn't valid JSON.", ex);
            }
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in trimmed.Split('\n', '\r'))
        {
            Match match = OptionLine.Match(line);
            if (match.Success)
            {
                options[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        if (!options.Keys.Any(key => key.StartsWith("sites", StringComparison.Ordinal)))
        {
            throw new FormatException("This doesn't look like a LeechBlock options file.");
        }

        return ReadOptions(options, valuesAreJson: false);
    }

    /// <summary>Reads LeechBlock's extension storage, where every value is JSON.</summary>
    public static IReadOnlyList<ImportedBlockList> ReadStorage(IReadOnlyDictionary<string, string> storage)
    {
        return ReadOptions(storage, valuesAreJson: true);
    }

    private static IReadOnlyList<ImportedBlockList> ReadOptions(IReadOnlyDictionary<string, string> options, bool valuesAreJson)
    {
        var lists = new List<ImportedBlockList>();
        for (int set = 1; set <= MaxSets; set++)
        {
            string sites = Text(options, $"sites{set}", valuesAreJson);
            if (sites.Trim().Length == 0)
            {
                continue;
            }

            string name = Text(options, $"setName{set}", valuesAreJson).Trim();
            if (name.Length == 0)
            {
                name = $"Block Set {set}";
            }

            var notes = new List<string>();
            var block = new List<string>();
            var allow = new List<string>();
            foreach (string entry in sites.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.StartsWith('#') || entry is "FILE" or "+FILE")
                {
                    continue;
                }

                if (entry.StartsWith('~') || entry.StartsWith('>'))
                {
                    notes.Add($"{name}: '{entry}' (keyword or referrer entry) can't be imported.");
                }
                else if (entry.StartsWith('+'))
                {
                    allow.Add(entry[1..]);
                }
                else
                {
                    block.Add(entry);
                }
            }

            bool[] days = Days(options, $"days{set}", valuesAreJson);
            var schedule = new List<WeekRange>();
            foreach (string period in Text(options, $"times{set}", valuesAreJson).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                (int Start, int End)? minutes = Minutes(period);
                if (minutes is not { } range)
                {
                    notes.Add($"{name}: time period '{period}' can't be read.");
                    continue;
                }

                for (int day = 0; day < 7; day++)
                {
                    if (days[day])
                    {
                        schedule.AddRange(WeekRange.FromWeekMinutes(day * WeekRange.MinutesPerDay + range.Start, day * WeekRange.MinutesPerDay + range.End));
                    }
                }
            }

            if (Text(options, $"limitMins{set}", valuesAreJson).Trim().Length > 0)
            {
                notes.Add($"{name}: its time limit isn't imported (only its blocked times are).");
            }

            lists.Add(new ImportedBlockList(ImportSource.LeechBlock, name, block, allow, schedule, null, notes));
        }

        return lists;
    }

    /// <summary>"0900-1700" → (540, 1020); "0000-2400" is the whole day.</summary>
    private static (int Start, int End)? Minutes(string period)
    {
        Match match = TimePeriod.Match(period);
        if (!match.Success)
        {
            return null;
        }

        int start = ToMinutes(match.Groups[1].Value);
        int end = ToMinutes(match.Groups[2].Value);
        return start < end && end <= WeekRange.MinutesPerDay ? (start, end) : null;
    }

    private static int ToMinutes(string hhmm)
    {
        return int.Parse(hhmm[..2], CultureInfo.InvariantCulture) * 60 + int.Parse(hhmm[2..], CultureInfo.InvariantCulture);
    }

    private static bool[] Days(IReadOnlyDictionary<string, string> options, string key, bool valuesAreJson)
    {
        var days = new[] { false, true, true, true, true, true, false }; // LeechBlock's default: Mon–Fri.
        if (!options.TryGetValue(key, out string? raw))
        {
            return days;
        }

        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mask))
        {
            for (int i = 0; i < 7; i++)
            {
                days[i] = (mask & (1 << i)) != 0;
            }

            return days;
        }

        if (valuesAreJson)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    int i = 0;
                    foreach (JsonElement day in document.RootElement.EnumerateArray().Take(7))
                    {
                        days[i++] = day.ValueKind == JsonValueKind.True;
                    }
                }
            }
            catch (JsonException)
            {
                // Keep the default days.
            }
        }

        return days;
    }

    private static string Text(IReadOnlyDictionary<string, string> options, string key, bool valuesAreJson)
    {
        if (!options.TryGetValue(key, out string? raw))
        {
            return string.Empty;
        }

        if (!valuesAreJson)
        {
            return raw.Replace("\\n", "\n", StringComparison.Ordinal);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => document.RootElement.GetString() ?? string.Empty,
                JsonValueKind.Number => document.RootElement.GetRawText(),
                _ => string.Empty,
            };
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
