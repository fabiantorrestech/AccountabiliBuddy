namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One item of a list the user's devices share (ADR 48): a stable id, an edit time, and, once deleted, a tombstone. The
/// phone's Kotlin twin is <c>SharedItem</c>; <see cref="SharedListMerge"/> and its twin give the same result for the same items.
/// </summary>
public interface ISharedItem
{
    Guid Id { get; }

    /// <summary>When the item was last changed (created, edited, ticked, moved or deleted).</summary>
    DateTimeOffset EditedAt { get; }

    /// <summary>Set on a tombstone: when the item was deleted. A tombstone carries no text.</summary>
    DateTimeOffset? DeletedAt { get; }

    /// <summary>Where the item sits in its list: lower first. Fractions let an item go between two others without touching them.</summary>
    double Position { get; }

    /// <summary>
    /// The item's fields as one string, for the rare tie of two different versions with the same edit time, so every device
    /// picks the same one. Never logged or shown.
    /// </summary>
    string TieBreak { get; }
}
