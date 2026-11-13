using System.Globalization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The fixed vocabulary and bounds of sync receipts (ADR 86) and the clean-up applied to a file as it opens: a code this app does not
/// know is dropped (a newer app's), the lists are cut to their bounds, an entry for no device is dropped. Dropping never deletes data.
/// </summary>
public static class SyncReceiptParts
{
    public const string Folder = "folder";
    public const string Drive = "drive";
    public const string Relay = "relay";
    public const string Qr = "qr";
    public const string File = "file";

    /// <summary>The sync methods a receipt tells apart (the link was deleted in ADR 45).</summary>
    public static readonly IReadOnlyList<string> Methods = [Folder, Drive, Relay, Qr, File];

    /// <summary>The reasons a read attempt failed (<see cref="SyncFileOpenFailure"/>).</summary>
    public static readonly IReadOnlyList<string> Failures = ["damaged", "otherGroup", "tooBig", "newerFormat"];

    public const string Taken = "taken";
    public const string Held = "held";
    public const string Off = "off";
    public const string Absent = "absent";
    public const string Refused = "refused";

    /// <summary>The outcomes of an accept, per part.</summary>
    public static readonly IReadOnlyList<string> States = [Taken, Held, Off, Absent, Refused];

    /// <summary>The parts of a sync file a receipt reports on (tasks = tasks, labels, views, task log and the account's task settings; goals = the five goal parts).</summary>
    public static readonly IReadOnlyList<string> Codes = ["sessions", "dayPlan", "settings", "tasks", "oldLists", "goals", "goalHistory", "steps", "lunch", "actions"];

    public const int MaxEntries = 16;
    public const int MaxMethods = 5;
    public const int MaxParts = 12;
    public const int MaxMonths = 3;

    /// <summary>
    /// The receipts as a reader keeps them: no entry for no device or twice for one (the first stays), only known methods, reasons, parts
    /// and states, a month only as "yyyy-MM" (the newest <see cref="MaxMonths"/>), every list within its bound. An entry that ends up
    /// with nothing but its device and time stays (it still says what is held). Null stays null.
    /// </summary>
    public static IReadOnlyList<SyncReceipt>? Clean(IReadOnlyList<SyncReceipt>? receipts)
    {
        if (receipts is null)
        {
            return null;
        }

        var seen = new HashSet<Guid>();
        var clean = new List<SyncReceipt>();
        foreach (SyncReceipt? receipt in receipts)
        {
            if (receipt is null || receipt.From == Guid.Empty || clean.Count >= MaxEntries || !seen.Add(receipt.From))
            {
                continue;
            }

            clean.Add(receipt with
            {
                ByMethod = NonEmpty(receipt.ByMethod?.Where(pair => Methods.Contains(pair.Key)).Take(MaxMethods).ToDictionary(pair => pair.Key, pair => pair.Value)),
                Failed = NonEmpty(receipt.Failed?.Where(pair => Methods.Contains(pair.Key) && pair.Value is not null && Failures.Contains(pair.Value)).Take(MaxMethods).ToDictionary(pair => pair.Key, pair => pair.Value)),
                Parts = NonEmpty(receipt.Parts?.Where(pair => Codes.Contains(pair.Key) && pair.Value is { State: not null } && States.Contains(pair.Value.State)).Take(MaxParts).ToDictionary(pair => pair.Key, pair => pair.Value)),
                History = CleanMonths(receipt.History),
            });
        }

        return clean;
    }

    /// <summary>Whether <paramref name="month"/> is "yyyy-MM" with a real month.</summary>
    public static bool IsMonth(string? month)
    {
        return month is { Length: 7 } && month[4] == '-'
            && int.TryParse(month.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year) && year is >= 2000 and <= 9999
            && int.TryParse(month.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number is >= 1 and <= 12;
    }

    private static IReadOnlyList<SyncHistoryReceipt>? CleanMonths(IReadOnlyList<SyncHistoryReceipt>? history)
    {
        if (history is null)
        {
            return null;
        }

        List<SyncHistoryReceipt> months = history
            .Where(item => item is not null && IsMonth(item.Month))
            .GroupBy(item => item.Month)
            .Select(group => group.First())
            .OrderByDescending(item => item.Month, StringComparer.Ordinal)
            .Take(MaxMonths)
            .ToList();
        return months.Count == 0 ? null : months;
    }

    private static IReadOnlyDictionary<string, T>? NonEmpty<T>(Dictionary<string, T>? items)
    {
        return items is { Count: > 0 } ? items : null;
    }
}
