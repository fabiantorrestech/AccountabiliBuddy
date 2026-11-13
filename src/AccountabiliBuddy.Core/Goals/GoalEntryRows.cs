using System.Text.Json;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The compact row form of a goal entry (ADR 79 size decision, GS1b): how <c>goalEntries</c> travels in the sync files and in the goal
/// history files (<see cref="GoalHistoryMonth"/>, <see cref="GoalHistoryYear"/>). A row is a JSON object with one-letter names, defaults left
/// out, written in this order (the phone writes the same text):
/// <list type="table">
/// <item><term><c>i</c></term><description>id, as <see cref="GuidText"/>; left out of a derived row whose id is its deterministic one
/// (<see cref="GoalDerivedIds.IsDerivedId"/>: rebuilt from goal, source, device and day).</description></item>
/// <item><term><c>g</c></term><description>goal id (<see cref="GuidText"/>); its presence is what makes an object a row.</description></item>
/// <item><term><c>d</c></term><description>day, the number yyyyMMdd (20261110).</description></item>
/// <item><term><c>t</c></term><description>time of day as .NET ticks of the day (100 ns since midnight); left out when none.</description></item>
/// <item><term><c>v</c></term><description>value, a JSON number written the shortest way that reads back exactly; left out when 0.</description></item>
/// <item><term><c>s</c></term><description>source code (<see cref="CodeOf"/>): <c>hc</c>, <c>st</c>, <c>bm</c>, <c>ca</c>, <c>fl</c>; left out for a hand entry.</description></item>
/// <item><term><c>w</c></term><description>the measuring device's id (<see cref="GuidText"/>); left out when none.</description></item>
/// <item><term><c>e</c></term><description>edit time: UTC .NET ticks (100 ns since 0001-01-01T00:00:00Z), a 64-bit integer (never read as a
/// double). Kotlin: <c>(epochSecond * 10_000_000 + nano / 100) + 621_355_968_000_000_000</c>.</description></item>
/// <item><term><c>x</c></term><description>deletion time of a tombstone, the same way; left out when live.</description></item>
/// <item><term><c>o</c></term><description>the times' UTC offset in whole minutes, as written by the device; left out when 0.</description></item>
/// <item><term><c>p</c></term><description>position; left out when 0.</description></item>
/// </list>
/// The object form (the GS1a JSON of <see cref="GoalEntry"/>) is written instead when a row can't hold the entry exactly: unknown fields
/// (<see cref="GoalEntry.Extra"/>), edit and deletion times with different offsets, or an offset that isn't whole minutes. A newer app adding
/// a source must write its entries in the object form (an older reader then keeps them as written and merges them by id and stamps). A reader
/// takes both forms: an object with <c>g</c> is a row; a row with a source code or a name it doesn't know is a newer item, kept as written
/// (<see cref="GoalNewerValueException"/>). Round trip exact: every field, tick precision, the offset.
/// </summary>
public static class GoalEntryRows
{
    private const long UnixEpochTicks = 621_355_968_000_000_000;

    private static readonly HashSet<string> Names = ["i", "g", "d", "t", "v", "s", "w", "e", "x", "o", "p"];

    private static readonly (GoalEntrySource Source, string Code)[] Codes =
    [
        (GoalEntrySource.HealthConnect, "hc"),
        (GoalEntrySource.ScreenTime, "st"),
        (GoalEntrySource.Beeminder, "bm"),
        (GoalEntrySource.Calendar, "ca"),
        (GoalEntrySource.FocusLocal, "fl"),
    ];

    /// <summary>Whether <paramref name="json"/> is written as a row (an object with <c>g</c>), not the object form.</summary>
    public static bool IsRow(JsonElement json)
    {
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("g", out _);
    }

    /// <summary>Whether <paramref name="entry"/> fits a row exactly (no unknown fields, one whole-minute offset for its times).</summary>
    public static bool FitsRow(GoalEntry entry)
    {
        if (entry.Extra is { Count: > 0 } || (CodeOf(entry.Source) is null && entry.Source != GoalEntrySource.Hand))
        {
            return false;
        }

        TimeSpan offset = entry.EditedAt.Offset;
        return offset.Ticks % TimeSpan.TicksPerMinute == 0 && (entry.DeletedAt is not DateTimeOffset deleted || deleted.Offset == offset);
    }

    /// <summary>The source's row code; null for a hand entry (left out) and a source with no code.</summary>
    public static string? CodeOf(GoalEntrySource source)
    {
        foreach ((GoalEntrySource known, string code) in Codes)
        {
            if (known == source)
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>
    /// An id as a row writes it: the 16 bytes in RFC 4122 order (as <see cref="Guid.ToByteArray(bool)"/> with big-endian; Kotlin: most
    /// significant long then least), base64url without padding (22 characters).
    /// </summary>
    public static string GuidText(Guid id)
    {
        return Convert.ToBase64String(id.ToByteArray(bigEndian: true)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Reads <see cref="GuidText"/>; null when it isn't 22 base64url characters.</summary>
    public static Guid? ReadGuid(string? text)
    {
        if (text is null || text.Length != 22 || text.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            return null;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + "==");
            return bytes.Length == 16 ? new Guid(bytes, bigEndian: true) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>A time as a row writes it: UTC .NET ticks.</summary>
    public static long TicksOf(DateTimeOffset time)
    {
        return time.UtcTicks;
    }

    /// <summary>Writes <paramref name="entry"/> as a row, or in the object form when it doesn't fit one (<see cref="FitsRow"/>).</summary>
    public static void Write(Utf8JsonWriter writer, GoalEntry entry, JsonSerializerOptions options)
    {
        if (!FitsRow(entry))
        {
            JsonSerializer.Serialize(writer, entry, options);
            return;
        }

        writer.WriteStartObject();
        if (!GoalDerivedIds.IsDerivedId(entry))
        {
            writer.WriteString("i", GuidText(entry.Id));
        }

        writer.WriteString("g", GuidText(entry.GoalId));
        writer.WriteNumber("d", DayNumber(entry.Day));
        if (entry.Time is TimeOnly time)
        {
            writer.WriteNumber("t", time.Ticks);
        }

        if (entry.Value != 0)
        {
            writer.WriteNumber("v", entry.Value);
        }

        if (CodeOf(entry.Source) is string code)
        {
            writer.WriteString("s", code);
        }

        if (entry.DeviceId is Guid device)
        {
            writer.WriteString("w", GuidText(device));
        }

        writer.WriteNumber("e", TicksOf(entry.EditedAt));
        if (entry.DeletedAt is DateTimeOffset deleted)
        {
            writer.WriteNumber("x", TicksOf(deleted));
        }

        int offsetMinutes = (int)(entry.EditedAt.Offset.Ticks / TimeSpan.TicksPerMinute);
        if (offsetMinutes != 0)
        {
            writer.WriteNumber("o", offsetMinutes);
        }

        if (entry.Position != 0)
        {
            writer.WriteNumber("p", entry.Position);
        }

        writer.WriteEndObject();
    }

    /// <summary>The JSON text <see cref="Write"/> gives for one entry.</summary>
    public static string Text(GoalEntry entry)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, entry, SyncJson.Options);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads a row (<see cref="IsRow"/>).</summary>
    /// <exception cref="GoalNewerValueException">A source code or a name this app doesn't know (a newer app's row: keep it as written).</exception>
    /// <exception cref="JsonException">A damaged row (a missing or unreadable field, a derived row whose id can't be rebuilt).</exception>
    public static GoalEntry Read(JsonElement row)
    {
        foreach (JsonProperty property in row.EnumerateObject())
        {
            if (!Names.Contains(property.Name))
            {
                throw new GoalNewerValueException($"A goal entry row field this app doesn't know: {property.Name}.");
            }
        }

        GoalEntrySource source = GoalEntrySource.Hand;
        if (row.TryGetProperty("s", out JsonElement sourceValue))
        {
            string code = sourceValue.ValueKind == JsonValueKind.String ? sourceValue.GetString()! : throw new JsonException("Row source.");
            source = Codes.FirstOrDefault(item => item.Code == code) is { Code: not null } match
                ? match.Source
                : throw new GoalNewerValueException($"A goal entry source this app doesn't know: {code}.");
        }

        Guid goalId = Id(row, "g") ?? throw new JsonException("Row goal id.");
        DateOnly day = Day(row);
        TimeOnly? time = row.TryGetProperty("t", out JsonElement timeValue)
            ? Long(timeValue) is long timeTicks and >= 0 and < TimeSpan.TicksPerDay ? new TimeOnly(timeTicks) : throw new JsonException("Row time.")
            : null;
        double value = row.TryGetProperty("v", out JsonElement valueValue) ? Number(valueValue) : 0;
        Guid? device = row.TryGetProperty("w", out _) ? Id(row, "w") ?? throw new JsonException("Row device.") : null;
        int offsetMinutes = row.TryGetProperty("o", out JsonElement offsetValue)
            ? Long(offsetValue) is long minutes and >= -14 * 60 and <= 14 * 60 ? (int)minutes : throw new JsonException("Row offset.")
            : 0;
        TimeSpan offset = TimeSpan.FromMinutes(offsetMinutes);
        DateTimeOffset editedAt = Time(row, "e", offset) ?? throw new JsonException("Row edit time.");
        DateTimeOffset? deletedAt = row.TryGetProperty("x", out _) ? Time(row, "x", offset) ?? throw new JsonException("Row deletion time.") : null;
        double position = row.TryGetProperty("p", out JsonElement positionValue) ? Number(positionValue) : 0;

        Guid id;
        if (row.TryGetProperty("i", out _))
        {
            id = Id(row, "i") ?? throw new JsonException("Row id.");
        }
        else if (GoalDerivedWriter.IsDerived(source) && device is Guid measuredBy)
        {
            id = GoalDerivedIds.For(goalId, source, measuredBy, day);
        }
        else
        {
            throw new JsonException("A row without an id must be a device's derived value.");
        }

        return new GoalEntry(id, goalId, day, time, value, source, editedAt, position, deletedAt, device);
    }

    /// <summary>
    /// The id and stamps of a row read only that far (a newer app's row kept as written, <see cref="GoalRawItem"/>); null when its id or edit
    /// time can't be read (it is then carried along unchanged).
    /// </summary>
    internal static (Guid Id, DateTimeOffset EditedAt, DateTimeOffset? DeletedAt, double Position)? Stamps(JsonElement row)
    {
        try
        {
            Guid? id = Id(row, "i");
            int offsetMinutes = row.TryGetProperty("o", out JsonElement offsetValue) && Long(offsetValue) is long minutes and >= -14 * 60 and <= 14 * 60 ? (int)minutes : 0;
            TimeSpan offset = TimeSpan.FromMinutes(offsetMinutes);
            DateTimeOffset? editedAt = Time(row, "e", offset);
            if (id is null || editedAt is null)
            {
                return null;
            }

            DateTimeOffset? deletedAt = row.TryGetProperty("x", out _) ? Time(row, "x", offset) : null;
            double position = row.TryGetProperty("p", out JsonElement positionValue) && positionValue.ValueKind == JsonValueKind.Number ? positionValue.GetDouble() : 0;
            return (id.Value, editedAt.Value, deletedAt, position);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A day as a row writes it: the number yyyyMMdd.</summary>
    public static int DayNumber(DateOnly day)
    {
        return (day.Year * 10000) + (day.Month * 100) + day.Day;
    }

    private static DateOnly Day(JsonElement row)
    {
        if (!row.TryGetProperty("d", out JsonElement dayValue) || Long(dayValue) is not long number || number is < 10101 or > 99991231)
        {
            throw new JsonException("Row day.");
        }

        int year = (int)(number / 10000);
        int month = (int)(number / 100 % 100);
        int dayOfMonth = (int)(number % 100);
        if (month is < 1 or > 12 || dayOfMonth < 1 || dayOfMonth > DateTime.DaysInMonth(year, month))
        {
            throw new JsonException("Row day.");
        }

        return new DateOnly(year, month, dayOfMonth);
    }

    private static Guid? Id(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? ReadGuid(value.GetString()) : null;
    }

    private static DateTimeOffset? Time(JsonElement row, string name, TimeSpan offset)
    {
        if (!row.TryGetProperty(name, out JsonElement value) || Long(value) is not long ticks)
        {
            return null;
        }

        long local = ticks + offset.Ticks;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks || local < DateTime.MinValue.Ticks || local > DateTime.MaxValue.Ticks)
        {
            return null;
        }

        return new DateTimeOffset(ticks, TimeSpan.Zero).ToOffset(offset);
    }

    private static long? Long(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;
    }

    private static double Number(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number)
            ? number
            : throw new JsonException("Row number.");
    }
}
