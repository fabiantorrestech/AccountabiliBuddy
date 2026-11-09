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

    /// <summary>The same item with other edit and deletion times (<see cref="SharedListMerge"/> uses it to clamp a far-future stamp).</summary>
    ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt);

    /// <summary>
    /// The device that keeps this item as a local one (ADR 72); null = it syncs. Every device writes a local item as a stub
    /// (<see cref="DeletedAt"/> set, no text, this field kept), so an app that predates it drops the item as deleted; only the
    /// keeper holds the text.
    /// </summary>
    Guid? LocalTo => null;

    /// <summary>The item as the stub every device writes for a local item: no text, deleted at its own edit time, <see cref="LocalTo"/> kept.</summary>
    ISharedItem AsLocalStub() => this;
}
