using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>The parts of one kind of day (Workdays, Off-Days, or a one-off day), in no particular order.</summary>
/// <param name="Blocks">The day's block rules (schema v10, ADR 37); null = the defaults (<see cref="BlockRules.Default"/>).</param>
public sealed record DayTemplate(
    IReadOnlyList<PlanPart> Parts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BlockRules? Blocks = null)
{
    public static DayTemplate Empty { get; } = new(Array.Empty<PlanPart>());

    /// <summary>The block rules this day follows.</summary>
    [JsonIgnore]
    public BlockRules Rules => Blocks ?? BlockRules.Default;
}
