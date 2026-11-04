package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SharedListMerge
import com.accountabilibuddy.shared.sync.SyncCategory
import java.time.Instant

/**
 * The colour to show for a task category (ADR 62). A task, Later item or come-back keeps the colour it had when it was stored, so a
 * category recoloured since would show its old look. The colour the user chose last, on any device, wins: this phone's own list and
 * the lists in the other devices' files, by name in any case; a stamp further ahead than the shared lists allow doesn't count, and with no
 * stamp this phone's own colour comes first. A category no list has (deleted, or only ever seen on a task) keeps its stored colour.
 */
class TaskCategoryColors(own: List<SyncCategory>, received: List<ReceivedDevice>, now: Instant) {
    private val current: Map<String, SyncCategory>

    init {
        val limit = now.plus(SharedListMerge.FUTURE_LIMIT)
        current = (own + received.flatMap { it.file.categories.orEmpty() })
            .groupBy { it.name.lowercase() }
            .mapValues { (_, named) ->
                named.filter { stamp -> stamp.colorEditedAt?.isAfter(limit) == false }.maxByOrNull { it.colorEditedAt!! } ?: named.first()
            }
    }

    /** Every category the lists hold, with its current colour. */
    val listed: List<SyncCategory> get() = current.values.toList()

    /** The colour for something in category [name] that stored [stored]; both null for no category. */
    fun of(name: String?, stored: String?): String? = name?.let { current[it.lowercase()]?.color } ?: stored
}
