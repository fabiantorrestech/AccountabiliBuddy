using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>Today's flexible lunch as a device started it (schema v8): every device takes the newest edit for the day.</summary>
/// <param name="EditedAt">When it was last started, made longer or shorter, or ended early; null from a device that
/// doesn't send it (then <paramref name="Start"/> stands for it).</param>
public sealed record SyncLunch(
    DateTimeOffset Start,
    DateTimeOffset End,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? EditedAt = null)
{
    /// <summary>What two devices' lunches are compared by: the newer one wins.</summary>
    [JsonIgnore]
    public DateTimeOffset LastEdit => EditedAt ?? Start;
}
