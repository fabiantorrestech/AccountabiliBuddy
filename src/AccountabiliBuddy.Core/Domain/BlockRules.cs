namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A day's block rules (schema v10, ADR 37): the blocks it keeps on (<see cref="Guarded"/>; null = every block the
/// device saw on when rules were first set), when anything guarded that's off comes back on (<see cref="ReenableAt"/>,
/// 00:00 = the end of the day), the heads-up before it on the blocker's device (<see cref="WarnMinutes"/>; null = none)
/// and the lift windows. Schema v11: a time per device (<see cref="DeviceTimes"/>, else <see cref="ReenableAt"/>) and
/// blocks with their own time (<see cref="BlockTimes"/>, left out of their device's).
/// </summary>
public sealed record BlockRules(
    IReadOnlyList<BlockRef>? Guarded,
    TimeOnly ReenableAt,
    int? WarnMinutes,
    IReadOnlyList<BlockLift> Lifts,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<DeviceReenable>? DeviceTimes = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<BlockReenable>? BlockTimes = null)
{
    /// <summary>When <paramref name="device"/>'s blocks come back on.</summary>
    public TimeOnly ReenableFor(string device) => DeviceTimes?.FirstOrDefault(d => d.Device == device)?.At ?? ReenableAt;

    /// <summary>Everything seen on stays on: back on at midnight, a heads-up 5 minutes before, no lift windows.</summary>
    public static BlockRules Default { get; } = new(null, TimeOnly.MinValue, 5, Array.Empty<BlockLift>());
}
