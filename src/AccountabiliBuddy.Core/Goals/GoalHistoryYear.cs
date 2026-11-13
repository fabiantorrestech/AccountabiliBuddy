using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The goal history of one finished UTC year (ADR 79 shared history, GS1b): the roll-up of every device's month files of that year
/// (<see cref="GoalHistoryRules.RollUp"/>), written by the PC with the lowest device id (<see cref="GoalHistoryRules.Roller"/>). A roll-up only
/// adds: every entry id of an earlier version stays (a newer version of an entry may replace an older one, as in any merge), and each month
/// file it took is listed with the version taken. Sealed by <see cref="GoalHistoryCodec"/>; never in a QR file.
/// </summary>
/// <param name="FormatVersion"><see cref="CurrentFormatVersion"/> when written; a reader refuses a newer one.</param>
/// <param name="Year">The UTC year.</param>
/// <param name="RolledBy">The PC that rolled this version.</param>
/// <param name="RolledAt">When this version was rolled: names the version (a device's <see cref="SyncGoalYearMerged"/> marker).</param>
/// <param name="Months">The month files taken, by device then month.</param>
/// <param name="Entries">Ordered by id; JSON <c>entries</c> as rows (<see cref="SyncGoalEntryListConverter"/>).</param>
public sealed record GoalHistoryYear(
    int FormatVersion,
    int Year,
    Guid RolledBy,
    DateTimeOffset RolledAt,
    IReadOnlyList<GoalHistoryPiece> Months,
    [property: JsonConverter(typeof(SyncGoalEntryListConverter))] IReadOnlyList<GoalEntry> Entries)
{
    public const int CurrentFormatVersion = 1;

    /// <summary>The file's key in a listing (<see cref="GoalHistoryRules.ToRead"/>).</summary>
    [JsonIgnore]
    public GoalHistoryFileKey Key => GoalHistoryFileKey.ForYear(Year, RolledBy);
}
