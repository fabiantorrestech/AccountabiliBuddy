package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * One item of a list the user's devices share (ADR 48): a stable id, an edit time and, once deleted, a tombstone. The
 * PC's twin is Core's ISharedItem; [SharedListMerge] and the PC's SharedListMerge give the same result for the same items.
 */
interface SharedItem {
    /** The id in its 36-character form; compared lower-case. */
    val id: String

    /** When the item was last changed (created, edited, ticked, moved or deleted). */
    val editedAt: Instant

    /** Set on a tombstone: when the item was deleted. A tombstone carries no text. */
    val deletedAt: Instant?

    /** Where the item sits in its list: lower first. Fractions let an item go between two others without touching them. */
    val position: Double

    /**
     * The item's fields as one string, for the rare tie of two different versions with the same edit time, so every
     * device picks the same one. Built exactly as the PC builds it. Never logged or shown.
     */
    val tieBreak: String

    /** The same item with other edit and deletion times ([SharedListMerge] uses it to clamp a far-future stamp). */
    fun withStamps(editedAt: Instant, deletedAt: Instant?): SharedItem
}

/** Time as .NET counts it (100 ns ticks since 0001-01-01 UTC), so times compare as they do on the PC. */
internal fun Instant.ticks(): Long = (epochSecond + 62_135_596_800L) * 10_000_000L + nano / 100
