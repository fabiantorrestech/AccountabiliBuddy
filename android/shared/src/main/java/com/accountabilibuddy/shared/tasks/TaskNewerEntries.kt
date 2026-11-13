package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/**
 * A newer app's entries the phone keeps as written ([TaskRead.newer]: a bin, kind or status this version doesn't know), so they are
 * passed on, never merged or shown. Each incoming file replaces them by id (R7.1 review): an entry of the same id in the file (as a
 * newer entry again, or as one this version can read, a tombstone included) takes the held one's place, so a frozen copy can never
 * outlive the newer app's own later edit or 30-day tombstone. Entries the file doesn't mention are kept.
 */
object TaskNewerEntries {
    /** The held newer entries of one part after a file's: [incomingKnownIds] are the ids of the file's readable entries of that part. */
    fun merge(held: List<JsonObject>, incomingNewer: List<JsonObject>, incomingKnownIds: Set<String>, wrapper: String): List<JsonObject> {
        val replaced = incomingNewer.mapNotNull { idOf(it, wrapper) }.toSet() + incomingKnownIds.map { it.lowercase() }
        val kept = held.filter { entry -> idOf(entry, wrapper)?.let { it !in replaced } ?: true }
        val incoming = incomingNewer.filter { idOf(it, wrapper) != null }.associateBy { idOf(it, wrapper)!! }.values
        return kept + incoming
    }

    /** The parts' newer entries after a file's parts: each part replaced by id; a part the file doesn't carry leaves its entries alone. */
    fun merge(held: TaskParts, incoming: TaskParts): TaskParts = held.copy(
        newerTasks = incoming.tasks?.let { known -> merge(held.newerTasks, incoming.newerTasks, known.map { it.id }.toSet(), TASK) } ?: held.newerTasks,
        newerLabels = incoming.labels?.let { known -> merge(held.newerLabels, incoming.newerLabels, known.map { it.id }.toSet(), LABEL) } ?: held.newerLabels,
        newerViews = incoming.views?.let { known -> merge(held.newerViews, incoming.newerViews, known.map { it.id }.toSet(), VIEW) } ?: held.newerViews,
        newerTaskLog = incoming.taskLog?.let { known -> merge(held.newerTaskLog, incoming.newerTaskLog, known.map { it.id }.toSet(), ENTRY) } ?: held.newerTaskLog,
    )

    const val TASK = "task"
    const val LABEL = "label"
    const val VIEW = "view"
    const val ENTRY = "entry"

    // The entry's id, lower case: inside its wrapper object ("task", "label", "view", "entry"); null when it has none.
    private fun idOf(entry: JsonObject, wrapper: String): String? =
        ((entry[wrapper] as? JsonObject)?.get("id") as? JsonPrimitive)?.takeIf { it.isString }?.content?.lowercase()
}
