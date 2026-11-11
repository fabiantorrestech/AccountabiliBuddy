using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A task on the Later list (ADR 50), the shared list beside the next session's tasks (ADR 48): things the user keeps for
/// another day, with the steps, category, estimate and note they had and an optional due date. The sealed sync files share
/// it between the user's devices, item by item; a deleted item travels as a tombstone with no text. Only the sealed files
/// carry it; the text is never logged.
/// </summary>
/// <param name="Category">The category's name; null for none.</param>
/// <param name="CategoryColor">The category's colour, "#RRGGBB".</param>
/// <param name="Subtasks">Its steps in order; null for none.</param>
/// <param name="EstimateMinutes">The user's estimate in minutes; null for none.</param>
/// <param name="Note">The user's private note; null for none.</param>
/// <param name="DueOn">The local date it is due; null for none.</param>
/// <param name="PlannedFor">The day part it was put in the plan for; null for none. When that part starts, the task moves to the next session's list.</param>
/// <param name="DoneAt">When it was ticked off; null while it is open. A ticked item keeps its text and shows as done on every device; an app
/// that predates the field ignores it and passes the item on without it (a newer edit there brings it back as open). It ages out after 90 days.</param>
/// <param name="Bin">
/// ADR 76: <see cref="NextBin"/> when the task sits in Shelf, Next (which older apps show as Later); null for Later. Written by new PCs
/// for older apps only; an app that predates it drops it when it passes the item on, so a reader never takes its absence as a move.
/// </param>
public sealed record SyncLaterItem(
    Guid Id,
    string Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SessionSubtask>? Subtasks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CategoryColor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EstimateMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? DueOn,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt,
    double Position,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlannedSlot? PlannedFor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DoneAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Labels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LocalTo = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Bin = null) : ISharedItem
{
    /// <summary>The <see cref="Bin"/> marker of a task in Shelf, Next.</summary>
    public const string NextBin = "next";

    /// <summary>Whether it is ticked off.</summary>
    [JsonIgnore]
    public bool IsDone => DoneAt is not null;

    // The done time joins the tie-break only when there is one, so an item without it breaks ties exactly as before (an app that
    // predates the field), and a copy that kept it wins an equal-stamp tie against the same item passed on without it.
    [JsonIgnore]
    public string TieBreak => string.Join(
        "\u0001",
        new[]
        {
            Title,
            string.Join("\u0002", (Subtasks ?? []).Select(step => $"{step.Id:D}\u0003{step.Title}\u0003{step.DoneAt?.UtcTicks}")),
            Category ?? string.Empty, CategoryColor ?? string.Empty,
            EstimateMinutes?.ToString() ?? string.Empty, Note ?? string.Empty,
            DueOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            PlannedFor?.TieText ?? string.Empty,
        }.Concat(DoneAt is DateTimeOffset done ? [done.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)] : [])
         .Concat(SharedItemTie.Extra(Labels, LocalTo))
         .Concat(Bin is null ? [] : [Bin]));

    public ISharedItem AsLocalStub()
    {
        return Deleted(EditedAt) with { LocalTo = LocalTo };
    }

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }

    /// <summary>The item as a tombstone: its text is gone, and it is deleted at <paramref name="at"/>.</summary>
    public SyncLaterItem Deleted(DateTimeOffset at)
    {
        return this with
        {
            Title = string.Empty, Subtasks = null, Category = null, CategoryColor = null, EstimateMinutes = null, Note = null,
            DueOn = null, PlannedFor = null, DoneAt = null, Labels = null, LocalTo = null, Bin = null, EditedAt = at, DeletedAt = at,
        };
    }
}
