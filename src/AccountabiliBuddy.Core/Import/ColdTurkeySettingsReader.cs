using System.Globalization;
using System.Text;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// Reads Cold Turkey Blocker's blocks from its settings database (data-app.db, table settings, key "settings").
/// The stored value is "CTB", a short version, then hex in which every byte is shifted; inside is JSON with a
/// "blocks" object. The format is undocumented and may change with any Cold Turkey update, so every problem is
/// reported as a <see cref="FormatException"/> and the user can paste the lists instead.
/// </summary>
public static class ColdTurkeySettingsReader
{
    private const string Prefix = "CTB";
    private const int MaxVersionLength = 4;

    /// <summary>Reads a copy of data-app.db (Cold Turkey keeps the original open).</summary>
    public static IReadOnlyList<ImportedBlockList> ReadDatabase(string databasePath)
    {
        return ParseSettingsJson(Decode(ReadStoredSettings(databasePath)));
    }

    /// <summary>Whether each block is on, from a copy of data-app.db.</summary>
    public static ColdTurkeyBlocks ReadBlockStates(string databasePath)
    {
        return ParseBlockStates(Decode(ReadStoredSettings(databasePath)));
    }

    private static string ReadStoredSettings(string databasePath)
    {
        string? stored;
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = 'settings';";
            stored = command.ExecuteScalar() as string;
        }
        catch (SqliteException ex)
        {
            throw new FormatException("Cold Turkey's settings couldn't be read.", ex);
        }

        if (stored is null)
        {
            throw new FormatException("Cold Turkey has no saved settings.");
        }

        return stored;
    }

    /// <summary>
    /// Turns the stored value into JSON. Tries each place the hex could start after the prefix, with the byte shift
    /// that turns the first byte into '{', and keeps the first result that is a JSON object.
    /// </summary>
    public static string Decode(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException("This isn't a Cold Turkey settings value.");
        }

        for (int start = Prefix.Length; start <= Prefix.Length + MaxVersionLength; start++)
        {
            byte[]? bytes = TryParseHex(stored[start..]);
            if (bytes is not { Length: > 1 })
            {
                continue;
            }

            byte shift = unchecked((byte)(bytes[0] - (byte)'{'));
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = unchecked((byte)(bytes[i] - shift));
            }

            string text = Encoding.UTF8.GetString(bytes);
            if (IsJsonObject(text))
            {
                return text;
            }
        }

        throw new FormatException("Cold Turkey's settings are in a format this version can't read.");
    }

    public static IReadOnlyList<ImportedBlockList> ParseSettingsJson(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("blocks", out JsonElement blocks) || blocks.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Cold Turkey's settings have no blocks.");
            }

            return blocks.EnumerateObject()
                .Where(block => block.Value.ValueKind == JsonValueKind.Object)
                .Select(block => ReadBlock(block.Name, block.Value))
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new FormatException("Cold Turkey's settings aren't valid JSON.", ex);
        }
    }

    /// <summary>
    /// Each block's "enabled" says whether it's switched on; a "scheduled" block then blocks only during its
    /// "schedule". "additional.paused" pauses every block.
    /// </summary>
    public static ColdTurkeyBlocks ParseBlockStates(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("blocks", out JsonElement blocks) || blocks.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Cold Turkey's settings have no blocks.");
            }

            bool paused = root.TryGetProperty("additional", out JsonElement additional)
                          && additional.ValueKind == JsonValueKind.Object
                          && StringProperty(additional, "paused") == "true";
            List<ColdTurkeyBlockState> states = blocks.EnumerateObject()
                .Where(block => block.Value.ValueKind == JsonValueKind.Object)
                .Select(block => ReadLock(new ColdTurkeyBlockState(
                    block.Name,
                    StringProperty(block.Value, "enabled") == "true",
                    StringProperty(block.Value, "type") == "scheduled",
                    ReadSchedule(block.Value)), block.Value))
                .ToList();
            bool pro = root.TryGetProperty("additional", out JsonElement extra)
                       && extra.ValueKind == JsonValueKind.Object
                       && StringProperty(extra, "proStatus") == "pro";
            return new ColdTurkeyBlocks(paused, states, pro);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Cold Turkey's settings aren't valid JSON.", ex);
        }
    }

    /// <summary>
    /// A block's lock (ADR 39): "none", a time "yyyy,M,d,H,m" (locked until then), "password", or another kind the user
    /// chose in Cold Turkey; and "lockUnblock" (switch off when the lock ends).
    /// </summary>
    private static ColdTurkeyBlockState ReadLock(ColdTurkeyBlockState state, JsonElement block)
    {
        string? kind = StringProperty(block, "lock");
        bool unblocks = StringProperty(block, "lockUnblock") != "false";
        state = state with { UnblocksWhenLockEnds = unblocks };
        if (string.IsNullOrEmpty(kind) || kind == "none")
        {
            return state;
        }

        // After "-stop -password" Cold Turkey keeps "password" with the block off: only an "on" block is held.
        if (kind == "password")
        {
            return state with { PasswordLocked = state.IsSwitchedOn };
        }

        int[] parts = kind.Split(',').Select(part => int.TryParse(part, out int value) ? value : -1).ToArray();
        if (parts.Length == 5 && parts.All(part => part >= 0))
        {
            try
            {
                return state with { LockedUntil = new DateTime(parts[0], parts[1], parts[2], parts[3], parts[4], 0) };
            }
            catch (ArgumentOutOfRangeException)
            {
                // Falls through: an unreadable time is treated as a lock of another kind.
            }
        }

        return state with { OtherLock = kind };
    }

    /// <summary>A block's weekly schedule; entries that can't be read are left out.</summary>
    private static List<WeekRange> ReadSchedule(JsonElement block)
    {
        var schedule = new List<WeekRange>();
        if (!block.TryGetProperty("schedule", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return schedule;
        }

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            int? start = WeekMinute(StringProperty(entry, "startTime"));
            int? end = WeekMinute(StringProperty(entry, "endTime"));
            if (start is not null && end is not null)
            {
                int endMinute = end.Value <= start.Value ? end.Value + WeekRange.MinutesPerWeek : end.Value;
                schedule.AddRange(WeekRange.FromWeekMinutes(start.Value, endMinute));
            }
        }

        return schedule;
    }

    private static ImportedBlockList ReadBlock(string name, JsonElement block)
    {
        var notes = new List<string>();
        var blockEntries = new List<string>();
        foreach (string site in Strings(block, "web"))
        {
            if (!IsFileUrl(site))
            {
                blockEntries.Add(site);
            }
        }

        foreach (string app in Strings(block, "apps"))
        {
            string? path = AppPath(app);
            if (path is null)
            {
                notes.Add($"{name}: app entry '{app}' can't be imported.");
            }
            else
            {
                blockEntries.Add(path);
            }
        }

        List<string> allowEntries = Strings(block, "exceptions").Where(entry => !IsFileUrl(entry)).ToList();

        var schedule = new List<WeekRange>();
        var breaks = new List<string?>();
        if (block.TryGetProperty("schedule", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                int? start = WeekMinute(StringProperty(entry, "startTime"));
                int? end = WeekMinute(StringProperty(entry, "endTime"));
                if (start is null || end is null)
                {
                    notes.Add($"{name}: a schedule entry couldn't be read.");
                    continue;
                }

                int endMinute = end.Value <= start.Value ? end.Value + WeekRange.MinutesPerWeek : end.Value;
                schedule.AddRange(WeekRange.FromWeekMinutes(start.Value, endMinute));
                breaks.Add(StringProperty(entry, "break"));
            }
        }

        if (breaks.Count == 0)
        {
            breaks.Add(StringProperty(block, "break"));
        }

        return new ImportedBlockList(
            ImportSource.ColdTurkey, name, blockEntries, allowEntries, schedule, UnlockOptionsFor(breaks), notes);
    }

    /// <summary>
    /// Cold Turkey's "break" setting: "none", or e.g. "delay,10,m,20,…" (a break after a 10-minute wait). A delay
    /// becomes an unlock option with that wait (the longest if entries differ); otherwise the block has none.
    /// </summary>
    private static IReadOnlyList<UnlockOption> UnlockOptionsFor(IEnumerable<string?> breaks)
    {
        TimeSpan? longest = null;
        foreach (string? setting in breaks)
        {
            TimeSpan? wait = DelayOf(setting);
            if (wait is TimeSpan value && (longest is null || value > longest))
            {
                longest = value;
            }
        }

        return longest is TimeSpan delay
            ? new[] { new UnlockOption(Guid.NewGuid(), "Break (from Cold Turkey)", delay) }
            : Array.Empty<UnlockOption>();
    }

    private static TimeSpan? DelayOf(string? setting)
    {
        string[] parts = (setting ?? string.Empty).Split(',');
        if (parts.Length < 3 || parts[0] != "delay"
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
        {
            return null;
        }

        return parts[2] switch
        {
            "s" => TimeSpan.FromSeconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            _ => null,
        };
    }

    /// <summary>"d,h,m" with d = 0 for Sunday (Cold Turkey's Mon–Fri entries use 1–5).</summary>
    private static int? WeekMinute(string? value)
    {
        string[] parts = (value ?? string.Empty).Split(',');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hour)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minute)
            || day is < 0 or > 6 || hour is < 0 or > 24 || minute is < 0 or > 59)
        {
            return null;
        }

        return day * WeekRange.MinutesPerDay + hour * 60 + minute;
    }

    /// <summary>App entries are paths to an .exe, sometimes with a "file:" prefix; folders and Store apps aren't supported.</summary>
    private static string? AppPath(string entry)
    {
        string path = entry.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? entry[5..] : entry;
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    // Cold Turkey adds "file://*.*" as an exception to every block so local files stay open.
    private static bool IsFileUrl(string entry)
    {
        return entry.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Strings(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!.Trim())
            .Where(item => item.Length > 0)
            .ToList();
    }

    private static string? StringProperty(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static byte[]? TryParseHex(string hex)
    {
        int length = hex.Length - hex.Length % 2;
        try
        {
            return Convert.FromHexString(hex.AsSpan(0, length));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsJsonObject(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
