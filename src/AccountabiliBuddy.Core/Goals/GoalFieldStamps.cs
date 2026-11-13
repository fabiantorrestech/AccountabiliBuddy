using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A goal's per-part stamps (ADR 79, owner 2026-11-14), one per <see cref="GoalField"/>, stored on the goal (<see cref="Goal.Stamps"/>)
/// and carried in the sealed sync files. Optional: a goal without them counts every part as changed at its
/// <see cref="Goal.EditedAt"/> by nobody (<see cref="For"/>), so goals stored before need no migration. Stamps of parts a newer app
/// added are kept and written back (<see cref="Extra"/>).
/// </summary>
public sealed record GoalFieldStamps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? Look = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? Area = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? Target = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? SecondTarget = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? Sources = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? Pauses = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? ShowOnToday = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? ExtraTiles = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalFieldStamp? Notes = null)
{
    /// <summary>Stamps of parts a newer app added, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }

    public static GoalFieldStamps None { get; } = new();

    /// <summary>The parts in the order they merge.</summary>
    public static IReadOnlyList<GoalField> Fields { get; } = Enum.GetValues<GoalField>();

    /// <summary>Every part stamped the same (a new goal, or one stored before goals synced).</summary>
    public static GoalFieldStamps AllAt(GoalFieldStamp stamp)
    {
        return new GoalFieldStamps(stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp);
    }

    /// <summary>
    /// The stamps <paramref name="goal"/> merges with: its own, every part it doesn't have stamped at its <see cref="Goal.EditedAt"/> by
    /// nobody (a goal without stamps counts every part as changed then).
    /// </summary>
    public static GoalFieldStamps For(Goal goal)
    {
        var unstamped = new GoalFieldStamp(goal.EditedAt, Guid.Empty);
        if (goal.Stamps is not GoalFieldStamps own)
        {
            return AllAt(unstamped);
        }

        GoalFieldStamps all = own;
        foreach (GoalField field in Fields)
        {
            if (own.Of(field) is null)
            {
                all = all.With(field, unstamped);
            }
        }

        return all;
    }

    /// <summary>The newest of the stamps; null when there are none.</summary>
    [JsonIgnore]
    public DateTimeOffset? Newest => Fields.Select(Of).Where(stamp => stamp is not null).Select(stamp => (DateTimeOffset?)stamp!.At).Max();

    public GoalFieldStamp? Of(GoalField field)
    {
        return field switch
        {
            GoalField.Name => Name,
            GoalField.Look => Look,
            GoalField.Area => Area,
            GoalField.Target => Target,
            GoalField.SecondTarget => SecondTarget,
            GoalField.Sources => Sources,
            GoalField.Pauses => Pauses,
            GoalField.ShowOnToday => ShowOnToday,
            GoalField.ExtraTiles => ExtraTiles,
            GoalField.Notes => Notes,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    public GoalFieldStamps With(GoalField field, GoalFieldStamp? stamp)
    {
        return field switch
        {
            GoalField.Name => this with { Name = stamp },
            GoalField.Look => this with { Look = stamp },
            GoalField.Area => this with { Area = stamp },
            GoalField.Target => this with { Target = stamp },
            GoalField.SecondTarget => this with { SecondTarget = stamp },
            GoalField.Sources => this with { Sources = stamp },
            GoalField.Pauses => this with { Pauses = stamp },
            GoalField.ShowOnToday => this with { ShowOnToday = stamp },
            GoalField.ExtraTiles => this with { ExtraTiles = stamp },
            GoalField.Notes => this with { Notes = stamp },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }
}
