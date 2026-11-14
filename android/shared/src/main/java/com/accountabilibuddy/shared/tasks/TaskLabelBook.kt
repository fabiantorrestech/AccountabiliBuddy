package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime

/**
 * The labels while a migration works through items that only name their category (ADR 76; the PC's TaskLabelBook): finds a label
 * by name (case-insensitive) and makes one, with the name-based id, when a name is new.
 *
 * @param reviver The device a revived label's fresh "Rings on" stamp names.
 */
class TaskLabelBook(labels: List<TaskLabel>, private val reviver: String = FieldStamp.NOBODY) {
    private val held = labels.toMutableList()

    val labels: List<TaskLabel> get() = held

    /** How many labels [idForName] made. */
    var made: Int = 0
        private set

    /** The live label with this name. */
    fun named(name: String): TaskLabel? = held.firstOrNull { it.isLive && TaskLabel.sameName(it.name, name) }

    /**
     * The id of the label named [name]; a new label (colour [color] when it is valid, else the first unused one; stamped [stamp])
     * when there is none. Null for no name.
     */
    fun idForName(name: String?, color: String?, stamp: OffsetDateTime): String? {
        if (name == null || name.trim().isEmpty()) {
            return null
        }

        named(name)?.let { return it.id }

        val id = freeIdFor(name, held)
        val chosen = if (TaskLabelColors.isValid(color)) color!!.trim() else TaskLabelColors.firstUnused(held)
        val position = if (held.isEmpty()) 1.0 else held.maxOf { it.position } + 1
        val existing = held.indexOfFirst { it.id == id }
        val label = TaskLabel(id, name.trim(), chosen, position, stamp)
        if (existing >= 0) {
            // A tombstone (freeIdFor never gives a live label's id): a newer use brings the label back, ringing on every device,
            // stamped at the revive, so a device still holding the old label's ring list can't hand it back (TaskLabelMerge).
            val old = held[existing].editedAt
            val revived = if (DotNetTime.compare(stamp, old) > 0) stamp else DotNetTime.plusTick(old)
            held[existing] = label.copy(editedAt = revived, ringOn = null, ringOnStamp = FieldStamp(revived, reviver), onlyOn = null, onlyOnStamp = FieldStamp(revived, reviver))
        } else {
            held += label
        }

        made++
        return id
    }

    companion object {
        /**
         * The id for a new label named [name]: its name-based id ([TaskLabel.idFor]), unless a live label renamed since holds that
         * id; then the next free one of the name's numbered ids (the name with "#2", "#3"... appended before hashing). Never a live
         * label's id; the same labels give the same id on every device. A tombstone's id may be given (that label comes back).
         */
        fun freeIdFor(name: String, labels: List<TaskLabel>): String {
            var id = TaskLabel.idFor(name)
            var suffix = 2
            while (labels.any { it.id == id && it.isLive }) {
                id = TaskLabel.idFor(name.trim() + "#" + suffix)
                suffix++
            }

            return id
        }
    }
}
