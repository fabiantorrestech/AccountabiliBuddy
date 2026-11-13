package com.accountabilibuddy.shared.tasks

import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * The phone's one-time switch to the task list (ADR 76, R7.2), pure: the migration's result is taken only when every item read is
 * accounted for ([TaskMigrationCounts.isBalanced]); then it is normalized as the store keeps it (the PC's TaskStore.ActivateBlocking).
 * Otherwise nothing is written and the phone stays on its old stores. The log line holds counts only, never text.
 */
object TaskModelSwitch {
    /**
     * @property lists What the store writes, with its marker in the same commit; null: refused (counts don't balance), nothing written.
     * @property logLine Counts only.
     */
    data class Outcome(val lists: TaskLists?, val viewSearches: Map<String, String>, val logLine: String)

    fun run(input: TaskMigrationInput, self: String, numberingDevice: String?): Outcome =
        decide(TaskMigration.run(input), self, numberingDevice, input.now, input.zone)

    fun decide(result: TaskMigrationResult, self: String, numberingDevice: String?, now: OffsetDateTime, zone: ZoneId): Outcome {
        val line = describe(result.counts)
        if (!result.counts.isBalanced) {
            return Outcome(null, emptyMap(), "$line; counts don't balance: staying on the old lists (no marker written)")
        }

        return Outcome(TaskListRules.normalize(TaskLists.from(result), self, numberingDevice, now, zone), result.viewSearches, line)
    }

    fun describe(counts: TaskMigrationCounts): String {
        fun <K> join(values: Map<K, Int>, name: (K) -> String) = values.entries.joinToString(" ") { "${name(it.key)}=${it.value}" }
        return "read: ${join(counts.sources) { it }}; tombstones read: ${join(counts.sourceTombstones) { it }}; bins: ${join(counts.bins) { it.wire }}; " +
            "history=${counts.history} expired=${counts.expired} duplicates=${counts.duplicates} tombstones=${counts.tombstones} " +
            "labels=${counts.labels} labelsFromItems=${counts.labelsFromItems} views=${counts.views} balanced=${counts.isBalanced}"
    }
}
