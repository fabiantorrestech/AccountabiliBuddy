using System.Security.Cryptography;
using System.Text;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The opaque names of the goal history files (ADR 79 shared history, GS3b), the same on Drive and the relay apart from the extension:
/// 12 lower-case hex characters of a SHA-256 over the group id and the file's key (kind, device, year, month), like a sync file's name
/// (<see cref="SyncFolderService.FileNameFor"/>). A name carries nothing readable, and every device computes the same one, so a device
/// finds a file by the names of the devices and years it knows.
/// </summary>
public static class GoalHistoryNames
{
    /// <summary>The first year a history file can be named for (the goals arrived in 2026).</summary>
    public const int FirstYear = 2025;

    public static string NameFor(string groupId, GoalHistoryFileKey key, string extension)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("goalhist|" + groupId + "|" + KeyText(key)));
        return Convert.ToHexStringLower(hash.AsSpan(0, 6)) + extension;
    }

    /// <summary>The key as text, for state and names: <c>m|device|yyyy|mm</c> or <c>y|rolledBy|yyyy</c>.</summary>
    public static string KeyText(GoalHistoryFileKey key)
    {
        string device = (key.DeviceId ?? Guid.Empty).ToString("D");
        return key.Kind == GoalHistoryFileKind.Month
            ? $"m|{device}|{key.Year:D4}|{key.Month ?? 0:D2}"
            : $"y|{device}|{key.Year:D4}";
    }

    public static GoalHistoryFileKey? ParseKey(string text)
    {
        string[] parts = text.Split('|');
        if (parts.Length == 4 && parts[0] == "m" && Guid.TryParse(parts[1], out Guid device) && int.TryParse(parts[2], out int year) && int.TryParse(parts[3], out int month))
        {
            return GoalHistoryFileKey.ForMonth(device, year, month);
        }

        return parts.Length == 3 && parts[0] == "y" && Guid.TryParse(parts[1], out Guid rolledBy) && int.TryParse(parts[2], out int rolledYear)
            ? GoalHistoryFileKey.ForYear(rolledYear, rolledBy)
            : null;
    }

    /// <summary>The text a device's own month is kept under in the state.</summary>
    public static string MonthText(int year, int month) => $"{year:D4}-{month:D2}";

    /// <summary>
    /// Every file name the group's files can have: each device's twelve months and each device's year file, for the years from
    /// <see cref="FirstYear"/> to <paramref name="lastYear"/>. The listing is matched against it (a name not in it is not a history file
    /// this PC knows).
    /// </summary>
    public static IReadOnlyDictionary<string, GoalHistoryFileKey> Candidates(string groupId, string extension, IEnumerable<Guid> devices, int lastYear)
    {
        var names = new Dictionary<string, GoalHistoryFileKey>(StringComparer.Ordinal);
        foreach (Guid device in devices.Distinct())
        {
            for (int year = FirstYear; year <= lastYear; year++)
            {
                GoalHistoryFileKey yearKey = GoalHistoryFileKey.ForYear(year, device);
                names[NameFor(groupId, yearKey, extension)] = yearKey;
                for (int month = 1; month <= 12; month++)
                {
                    GoalHistoryFileKey monthKey = GoalHistoryFileKey.ForMonth(device, year, month);
                    names[NameFor(groupId, monthKey, extension)] = monthKey;
                }
            }
        }

        return names;
    }

    /// <summary>Whether sealed bytes are a goal history file (a month's or a year's header): a sync reader that finds one skips it.</summary>
    public static bool IsHistoryFile(ReadOnlySpan<byte> sealedBytes)
    {
        return sealedBytes.Length >= 4 && (sealedBytes[..4].SequenceEqual("ABM1"u8) || sealedBytes[..4].SequenceEqual("ABY1"u8));
    }
}
