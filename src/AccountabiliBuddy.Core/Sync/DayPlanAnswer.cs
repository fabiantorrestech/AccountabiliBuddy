using System.Text.Json;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>How the PC took a <see cref="DayPlanEdit"/> (ADR 19).</summary>
/// <param name="Status">A <see cref="DayPlanStatus"/> value.</param>
/// <param name="Message">For the device to show, e.g. why it was refused.</param>
/// <param name="CurrentVersion">On a conflict: the PC's plan version now.</param>
/// <param name="CurrentDayPlan">On a conflict: the PC's plan now.</param>
/// <param name="CurrentEditedAt">On a conflict: when the PC's plan last changed, if known.</param>
public sealed record DayPlanAnswer(
    string Status,
    string Message,
    string? CurrentVersion = null,
    JsonElement? CurrentDayPlan = null,
    DateTimeOffset? CurrentEditedAt = null);
