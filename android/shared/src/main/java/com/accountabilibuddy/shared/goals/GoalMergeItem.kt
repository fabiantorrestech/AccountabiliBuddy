package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID

/**
 * One item of a goal list as the goals sync merges it (ADR 79; the PC's `ISharedItem`): a stable id, an edit time, a tombstone time
 * and a place. Times keep their offset (the merged text must equal the PC's), which is why goals don't use the task lists'
 * `SharedItem` (instants). [GoalListMerge] merges lists of these; [GoalRawItem] is one a newer app wrote.
 */
interface GoalMergeItem {
    val id: UUID

    /** When the item was last changed. */
    val editedAt: OffsetDateTime

    /** Set on a tombstone. */
    val deletedAt: OffsetDateTime?

    /** Where the item sits in its list: lower first. */
    val position: Double

    /** The item as one text, for the rare tie of two versions with the same stamp, built as the PC builds it. Never logged or shown. */
    val tieBreak: String

    /** The same item with other edit and deletion times (the future clamp). */
    fun withStamps(editedAt: OffsetDateTime, deletedAt: OffsetDateTime?): GoalMergeItem
}
