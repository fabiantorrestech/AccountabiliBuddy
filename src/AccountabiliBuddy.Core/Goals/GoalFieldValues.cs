using System.Text;
using System.Text.Json;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Reads and copies one <see cref="GoalField"/> of a goal for the field-by-field merge (ADR 79): its value as one comparable text and
/// the goal with one part taken from another copy. The text is built from the goal's own JSON (<see cref="SyncJson.Options"/>, which the
/// phone writes byte for byte the same): for each of the part's JSON names, the name, "=" and the value as written ("-" when left out),
/// joined by a line feed. Equal texts are equal values. Never logged or shown.
/// </summary>
public static class GoalFieldValues
{
    /// <summary>The JSON names each part covers, in this order.</summary>
    public static IReadOnlyList<string> NamesOf(GoalField field)
    {
        return field switch
        {
            GoalField.Name => ["name"],
            GoalField.Look => ["icon", "color"],
            GoalField.Area => ["areaId", "position"],
            GoalField.Target => ["kind", "target"],
            GoalField.SecondTarget => ["secondTarget", "trackTimeOnly"],
            GoalField.Sources => ["sources"],
            GoalField.Pauses => ["pauses"],
            GoalField.ShowOnToday => ["showOnToday"],
            GoalField.ExtraTiles => ["extraTiles"],
            GoalField.Notes => ["notes"],
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    /// <summary>The part's value as one text.</summary>
    public static string Canonical(Goal goal, GoalField field)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(goal, SyncJson.Options));
        return Canonical(document.RootElement, field);
    }

    /// <summary>Every part's value as one text, from one serialization of the goal.</summary>
    public static Dictionary<GoalField, string> All(Goal goal)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(goal, SyncJson.Options));
        return GoalFieldStamps.Fields.ToDictionary(field => field, field => Canonical(document.RootElement, field));
    }

    /// <summary><paramref name="target"/> with <paramref name="field"/> taken from <paramref name="source"/>.</summary>
    public static Goal With(Goal target, Goal source, GoalField field)
    {
        return field switch
        {
            GoalField.Name => target with { Name = source.Name },
            GoalField.Look => target with { Icon = source.Icon, Color = source.Color },
            GoalField.Area => target with { AreaId = source.AreaId, Position = source.Position },
            GoalField.Target => target with { Kind = source.Kind, Target = source.Target },
            GoalField.SecondTarget => target with { SecondTarget = source.SecondTarget, TrackTimeOnly = source.TrackTimeOnly },
            GoalField.Sources => target with { Sources = source.Sources },
            GoalField.Pauses => target with { Pauses = source.Pauses },
            GoalField.ShowOnToday => target with { ShowOnToday = source.ShowOnToday },
            GoalField.ExtraTiles => target with { ExtraTiles = source.ExtraTiles },
            GoalField.Notes => target with { Notes = source.Notes },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static string Canonical(JsonElement goal, GoalField field)
    {
        var text = new StringBuilder();
        foreach (string name in NamesOf(field))
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(name).Append('=').Append(goal.TryGetProperty(name, out JsonElement value) ? value.GetRawText() : "-");
        }

        return text.ToString();
    }
}
