package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.goals.GoalJson
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.security.MessageDigest

/**
 * Reads and copies one [TaskField] of a task for the field-level merge (ADR 76; the PC's TaskFieldValues): its value as one
 * comparable text, the hash of a title or note, and the task with one part taken from another copy. The texts are the PC's own
 * (its JSON text for the list and group fields), so a full tie between two stamps breaks the same way on every device.
 */
object TaskFieldValues {
    private const val NULL_TEXT = "\u0000"

    /** The fields in the order they merge. */
    val ALL: List<TaskField> = TaskField.entries

    /** The field's value as one text: equal texts are equal values. Never logged or shown. */
    fun canonical(task: TaskItem, field: TaskField): String = when (field) {
        TaskField.TITLE -> task.title
        TaskField.NOTE -> task.note ?: NULL_TEXT
        TaskField.STEPS -> TaskJson.text(TaskJson.writeSteps(task.steps.orEmpty(), unknown = false))
        TaskField.LABEL -> task.labelId ?: ""
        TaskField.TAGS -> TaskJson.text(JsonArray(task.tags.orEmpty().map { JsonPrimitive(it) }))
        TaskField.BIN -> TaskJson.text(
            buildJsonObject {
                put("bin", task.bin.wire)
                put("prevBin", task.prevBin?.wire)
                put("position", GoalJson.number(task.position))
                put("doneAt", task.doneAt?.let(GoalJson::time))
            },
        )

        TaskField.REMINDERS -> TaskJson.text(TaskJson.writeReminders(task.reminders.orEmpty(), unknown = false))
        TaskField.ESTIMATE -> task.estimateMinutes?.toString() ?: ""
        TaskField.DUE -> TaskJson.text(
            buildJsonObject {
                put("dueOn", task.dueOn?.toString())
                put("dueAt", task.dueAt?.let(GoalJson::timeOfDay))
            },
        )

        TaskField.HIGHLIGHT -> if (task.highlight) "1" else "0"
        TaskField.CANT_MISS -> if (task.cantMiss) "1" else "0"
        TaskField.DEVICES -> TaskJson.text(JsonArray(task.devices.orEmpty().map { JsonPrimitive(it) }))
        TaskField.CALENDAR -> if (task.calendar) "1" else "0"
        TaskField.NUMBER -> TaskJson.text(task.number?.let { TaskJson.writeNumber(it, unknown = false) } ?: JsonNull)
        // The PC's PlannedSlot.TieText: the start to whole seconds.
        TaskField.PLAN -> task.plannedFor?.let { listOf(it.date, it.partId.lowercase(), it.start.take(8)).joinToString("\u0003") } ?: ""
    }

    /**
     * A title's or note's hash for [com.accountabilibuddy.shared.sync.FieldStamp.bases]: the first 16 lower-case hex digits of
     * SHA-256 over the UTF-8 text, or "-" for no text (a removed note). The same as the PC's.
     */
    fun hash(value: String?): String {
        if (value == null) {
            return "-"
        }

        val digest = MessageDigest.getInstance("SHA-256").digest(value.toByteArray(Charsets.UTF_8))
        val hex = StringBuilder(16)
        for (index in 0 until 8) {
            val byte = digest[index].toInt() and 0xFF
            hex.append(HEX[byte ushr 4]).append(HEX[byte and 0x0F])
        }

        return hex.toString()
    }

    /** The text a title or note field holds (null for no note), for [hash] and conflicts. */
    fun text(task: TaskItem, field: TaskField): String? = when (field) {
        TaskField.TITLE -> task.title
        TaskField.NOTE -> task.note
        else -> throw IllegalArgumentException("Only the title and the note are texts.")
    }

    /** [target] with [field] taken from [source]. */
    fun with(target: TaskItem, source: TaskItem, field: TaskField): TaskItem = when (field) {
        TaskField.TITLE -> target.copy(title = source.title)
        TaskField.NOTE -> target.copy(note = source.note)
        TaskField.STEPS -> target.copy(steps = source.steps)
        TaskField.LABEL -> target.copy(labelId = source.labelId)
        TaskField.TAGS -> target.copy(tags = source.tags)
        TaskField.BIN -> target.copy(bin = source.bin, prevBin = source.prevBin, position = source.position, doneAt = source.doneAt)
        TaskField.REMINDERS -> target.copy(reminders = source.reminders)
        TaskField.ESTIMATE -> target.copy(estimateMinutes = source.estimateMinutes)
        TaskField.DUE -> target.copy(dueOn = source.dueOn, dueAt = source.dueAt)
        TaskField.HIGHLIGHT -> target.copy(highlight = source.highlight)
        TaskField.CANT_MISS -> target.copy(cantMiss = source.cantMiss)
        TaskField.DEVICES -> target.copy(devices = source.devices)
        TaskField.CALENDAR -> target.copy(calendar = source.calendar)
        TaskField.NUMBER -> target.copy(number = source.number)
        TaskField.PLAN -> target.copy(plannedFor = source.plannedFor)
    }

    private const val HEX = "0123456789abcdef"
}
