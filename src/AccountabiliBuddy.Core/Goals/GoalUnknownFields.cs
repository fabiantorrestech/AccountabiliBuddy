using System.Text.Json;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The fields of a goal object this app doesn't know, kept as they were read and written back unchanged after its own fields (ADR 79
/// forward compatibility: an older app never strips what a newer one wrote). Null on an object that has none. Two are equal when they
/// hold the same names with the same JSON, so goal records stay comparable by value. May hold the user's private text (a newer app's
/// field): sealed at rest with the goal, never logged.
/// </summary>
public sealed class GoalUnknownFields : Dictionary<string, JsonElement>
{
    public GoalUnknownFields()
        : base(StringComparer.Ordinal)
    {
    }

    public override bool Equals(object? obj)
    {
        return obj is GoalUnknownFields other && other.Count == Count
            && this.All(pair => other.TryGetValue(pair.Key, out JsonElement value) && value.GetRawText() == pair.Value.GetRawText());
    }

    public override int GetHashCode()
    {
        return Count;
    }
}
