package com.accountabilibuddy.shared.tasks

/**
 * How many items the task migration (ADR 76) read and where they went, for the verification log (counts only, never text; the PC's
 * TaskMigrationCounts). Every item with text that was read is in exactly one place: a bin, History, skipped as expired, or folded
 * into a copy of the same id from another list ([isBalanced]).
 *
 * @property sources Items with text read per old list ("nextTasks", "later", "comebacks", "autoSaved").
 * @property sourceTombstones Tombstones and local stubs read per old list.
 * @property bins Live tasks made per bin.
 * @property history History entries made (finished).
 * @property expired Auto-saved items past their 30 days, not carried over.
 * @property duplicates Items whose id another list also had (the newer copy was kept).
 * @property tombstones Task tombstones written (old tombstones, local stubs and tasks moved to History).
 * @property labels Labels made from categories.
 * @property labelsFromItems Labels made from a category name only an item carried.
 * @property views Views made (the three built-ins and the presets).
 * @property unmappedStatuses Old filter statuses with no bin, left out of a view (the phone's presets have none: always 0).
 */
data class TaskMigrationCounts(
    val sources: Map<String, Int>,
    val sourceTombstones: Map<String, Int>,
    val bins: Map<TaskBin, Int>,
    val history: Int,
    val expired: Int,
    val duplicates: Int,
    val tombstones: Int,
    val labels: Int,
    val labelsFromItems: Int,
    val views: Int,
    val unmappedStatuses: Int,
) {
    /** Items with text read = live tasks + History + expired + duplicates. */
    val isBalanced: Boolean get() = sources.values.sum() == bins.values.sum() + history + expired + duplicates
}
