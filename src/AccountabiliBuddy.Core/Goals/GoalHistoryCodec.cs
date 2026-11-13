using System.Text.Json;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Seals and opens the goal history files (ADR 79 shared history, GS1b) exactly as <see cref="SyncFileCodec"/> seals a sync file (the
/// group's key, AES-256-GCM over raw-Deflated UTF-8 JSON in <see cref="SyncJson.Options"/>, the same 16 MB limit) under their own 4-byte
/// header: <c>ABM1</c> for a month file, <c>ABY1</c> for a year file. The phone writes and reads the same bytes.
/// </summary>
public static class GoalHistoryCodec
{
    private static readonly byte[] MonthMagic = "ABM1"u8.ToArray();
    private static readonly byte[] YearMagic = "ABY1"u8.ToArray();

    public static byte[] Seal(GoalHistoryMonth month, SyncKey key)
    {
        return SyncFileCodec.SealJson(JsonSerializer.SerializeToUtf8Bytes(month, SyncJson.Options), key, MonthMagic);
    }

    public static byte[] Seal(GoalHistoryYear year, SyncKey key)
    {
        return SyncFileCodec.SealJson(JsonSerializer.SerializeToUtf8Bytes(year, SyncJson.Options), key, YearMagic);
    }

    /// <returns>How many bytes <see cref="Seal(GoalHistoryMonth, SyncKey)"/> gives (without the key).</returns>
    public static int SealedLength(GoalHistoryMonth month)
    {
        return SyncFileCodec.SealedJsonLength(JsonSerializer.SerializeToUtf8Bytes(month, SyncJson.Options));
    }

    /// <returns>How many bytes <see cref="Seal(GoalHistoryYear, SyncKey)"/> gives (without the key).</returns>
    public static int SealedLength(GoalHistoryYear year)
    {
        return SyncFileCodec.SealedJsonLength(JsonSerializer.SerializeToUtf8Bytes(year, SyncJson.Options));
    }

    /// <returns>The month file, or null when it isn't one this group sealed, can't be read, or comes from a newer format.</returns>
    public static GoalHistoryMonth? OpenMonth(ReadOnlySpan<byte> sealedBytes, SyncKey key)
    {
        GoalHistoryMonth? month = Read<GoalHistoryMonth>(SyncFileCodec.OpenJson(sealedBytes, key, MonthMagic));
        return month is { FormatVersion: >= 1 and <= GoalHistoryMonth.CurrentFormatVersion, Month: >= 1 and <= 12, Entries: not null } && month.DeviceId != Guid.Empty
            ? month with { Entries = GoalSyncClean.Entries(month.Entries)! }
            : null;
    }

    /// <returns>The year file, or null when it isn't one this group sealed, can't be read, or comes from a newer format.</returns>
    public static GoalHistoryYear? OpenYear(ReadOnlySpan<byte> sealedBytes, SyncKey key)
    {
        GoalHistoryYear? year = Read<GoalHistoryYear>(SyncFileCodec.OpenJson(sealedBytes, key, YearMagic));
        return year is { FormatVersion: >= 1 and <= GoalHistoryYear.CurrentFormatVersion, Entries: not null, Months: not null }
            ? year with
            {
                Entries = GoalSyncClean.Entries(year.Entries)!,
                Months = year.Months.Where(piece => piece is not null && piece.DeviceId != Guid.Empty && piece.Month is >= 1 and <= 12).ToList(),
            }
            : null;
    }

    private static T? Read<T>(byte[]? json)
        where T : class
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, SyncJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }
    }
}
