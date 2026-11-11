using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A view's "Show only" part (ADR 76). Ticks in one list widen it (any of them can match); different lists narrow it (a task must
/// match each). Values are plain strings so a newer app's value survives in an older one: an unknown one matches nothing.
/// </summary>
/// <param name="Due">"overdue", "today", "tomorrow", "thisWeek", "none" (no due date), or "range:yyyy-MM-dd..yyyy-MM-dd".</param>
/// <param name="Time">Time limit buckets: "le10", "le25", "le50", "ge50", "none".</param>
/// <param name="Old">Untouched for: "1w", "2w", "4w", "8w".</param>
/// <param name="HighlightedOnly">Only highlighted tasks.</param>
public sealed record TaskViewFilter(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Due = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Time = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Old = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool HighlightedOnly = false)
{
    public static TaskViewFilter None { get; } = new();
}
