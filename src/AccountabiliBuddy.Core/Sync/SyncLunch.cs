using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Today's flexible lunch as a device started it (schema v8, split lunch ADR 88): every device takes the newest edit for the day.
/// <see cref="Start"/> and <see cref="End"/> are the current piece, or the last one once it is over, so an older app still reads a running
/// or ended lunch correctly. The fields after <see cref="EditedAt"/> come from ADR 88 and are left out when not set; an older app ignores them.
/// Equality is by value, the list included (a record's own equality would compare the list by reference).
/// </summary>
/// <param name="EditedAt">When it was last started, made longer or shorter, counted or discarded; null from a device that
/// doesn't send it (then <paramref name="Start"/> stands for it).</param>
/// <param name="Allowance">The day's lunch in minutes, fixed when the first piece starts (and moved by +5 and -5), so "40 left" is the same on
/// every device whatever its own length setting; null from an older app (missing means keep: see <see cref="LunchDay.Receive"/>).</param>
/// <param name="Counted">The earlier pieces that were counted (the current one is <paramref name="Start"/> to <paramref name="End"/>); null
/// when unknown (an older app), empty when there are none.</param>
/// <param name="Discarded">The only piece was discarded: the lunch is a zero-length record that still marks the day as taken, so a fixed lunch
/// never starts by itself again. False from an older app.</param>
public sealed record SyncLunch(
    DateTimeOffset Start,
    DateTimeOffset End,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? EditedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Allowance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<LunchPiece>? Counted = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Discarded = false)
{
    /// <summary>What two devices' lunches are compared by: the newer one wins.</summary>
    [JsonIgnore]
    public DateTimeOffset LastEdit => EditedAt ?? Start;

    public bool Equals(SyncLunch? other) =>
        other is not null && Start == other.Start && End == other.End && EditedAt == other.EditedAt && Allowance == other.Allowance
        && Discarded == other.Discarded && SamePieceList(Counted, other.Counted);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Start);
        hash.Add(End);
        hash.Add(EditedAt);
        hash.Add(Allowance);
        hash.Add(Discarded);
        hash.Add(Counted is null ? -1 : Counted.Count);
        foreach (LunchPiece piece in Counted ?? [])
        {
            hash.Add(piece);
        }

        return hash.ToHashCode();
    }

    internal static bool SamePieceList(IReadOnlyList<LunchPiece>? a, IReadOnlyList<LunchPiece>? b) =>
        a is null || b is null ? a is null && b is null : a.SequenceEqual(b);
}
