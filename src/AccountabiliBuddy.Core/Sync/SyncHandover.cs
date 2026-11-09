using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// An item on its way to the device the user chose to keep it (ADR 72), carried sealed in the sender's file until the keeper's
/// own file shows the item's stub at least as new as <see cref="EditedAt"/>. Not taken in <see cref="Kept"/>, it comes back to
/// the sender, local there. Exactly one of the item fields is set, the one <see cref="List"/> names.
/// </summary>
/// <param name="To">The keeper.</param>
/// <param name="Id">The item's id.</param>
/// <param name="List">Which list: <see cref="NextTasks"/>, <see cref="Later"/>, <see cref="Parked"/>, <see cref="AutoSaved"/> or <see cref="History"/>.</param>
/// <param name="EditedAt">The item's edit time when it was handed over.</param>
/// <param name="SentAt">When the sender first carried it (the 30 days run from then).</param>
public sealed record SyncHandover(
    Guid To,
    Guid Id,
    string List,
    DateTimeOffset EditedAt,
    DateTimeOffset SentAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncNextTask? NextTask = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncLaterItem? LaterItem = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncParked? ParkedItem = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncAutoSavedItem? AutoSavedItem = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncKeptPast? PastTask = null)
{
    public const string NextTasks = "nextTasks";
    public const string Later = "later";
    public const string Parked = "parked";
    public const string AutoSaved = "autoSaved";
    public const string History = "history";

    /// <summary>How long a sender carries a handover before taking the item back.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(30);
}
