using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One device's goal history file for one month (ADR 79 shared history, owner 2026-11-14; GS1b): every goal entry the device holds whose
/// stamp (edit time, or deletion time when later) falls in that UTC month (<see cref="GoalHistoryRules.MonthOf"/>), its own and the ones it
/// received (hand entries carry no writer, and holding all of them means a month survives on any device that saw it), as compact rows,
/// entry tombstones kept for good. Only its device writes it, only while the month is open (<see cref="GoalHistoryRules.MonthToWrite"/>);
/// sealed by <see cref="GoalHistoryCodec"/>, read by every device of the group, never in a QR file. Private text: none (ids, days, numbers,
/// times only), but sealed like the sync files.
/// </summary>
/// <param name="FormatVersion"><see cref="CurrentFormatVersion"/> when written; a reader refuses a newer one.</param>
/// <param name="Year">The UTC year.</param>
/// <param name="Month">The UTC month, 1-12.</param>
/// <param name="WrittenAt">When the device wrote this version (its trusted now); a later write of the same month gets a later time.</param>
/// <param name="Entries">Ordered by id (<c>D</c> text, ordinal); JSON <c>entries</c> as rows (<see cref="SyncGoalEntryListConverter"/>).</param>
public sealed record GoalHistoryMonth(
    int FormatVersion,
    Guid DeviceId,
    int Year,
    int Month,
    DateTimeOffset WrittenAt,
    [property: JsonConverter(typeof(SyncGoalEntryListConverter))] IReadOnlyList<GoalEntry> Entries)
{
    public const int CurrentFormatVersion = 1;

    /// <summary>The file's key in a listing (<see cref="GoalHistoryRules.ToRead"/>).</summary>
    [JsonIgnore]
    public GoalHistoryFileKey Key => GoalHistoryFileKey.ForMonth(DeviceId, Year, Month);
}
