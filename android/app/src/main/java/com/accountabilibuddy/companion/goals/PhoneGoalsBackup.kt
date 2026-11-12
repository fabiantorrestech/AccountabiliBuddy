package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLists
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/**
 * The phone's goals in a backup (ADR 71; round 2 R6), the PC's `GoalsExport` shape (`areas`, `goals`, `entries`, `reminders`,
 * `reviews`, `includesNotes`) as a `goals` part next to the task lists, in the same kinds: the task-lists backup, and synced settings
 * while the task lists are shared. Readable inside the backup like task titles (never the Keystore-sealed blobs, which no other phone
 * could open); deletions are left out. Goal notes, reminder reasons and review notes go in only when the backup has a password
 * ([includesNotes]); without one they are left out and a restore keeps this phone's. The phone's backups have no password today, so
 * they never carry them.
 */
object PhoneGoalsBackup {
    const val PART = "goals"

    /** The `goals` part, or null while the goals aren't ready (nothing to back up, or they can't be read). */
    fun export(store: PhoneGoalStore, includesNotes: Boolean): JsonObject? {
        if (store.status.value != PhoneGoalStatus.Ready) return null
        val lists = store.lists.value
        return buildJsonObject {
            put("areas", GoalJson.writeAreas(lists.areas.filter { it.isLive }))
            put("goals", GoalJson.writeGoals(lists.goals.filter { it.isLive }.map { if (includesNotes) it else it.copy(notes = null) }))
            put("entries", GoalJson.writeEntries(lists.entries.filter { it.isLive }))
            put("reminders", GoalJson.writeReminders(lists.reminders.filter { it.isLive }.map { if (includesNotes) it else it.copy(reason = null) }))
            put("reviews", GoalJson.writeReviews(lists.reviews.filter { it.isLive }.map { if (includesNotes) it else it.copy(notes = null) }))
            put("includesNotes", includesNotes)
        }
    }

    /** Restores a backup's `goals` part through the store's rules; a line for the restore summary. */
    fun import(store: PhoneGoalStore, part: JsonObject, restore: Boolean): String {
        val read = runCatching {
            GoalLists(
                areas = (part["areas"] as? JsonArray)?.let(GoalJson::readAreas).orEmpty(),
                goals = (part["goals"] as? JsonArray)?.let(GoalJson::readGoals).orEmpty(),
                entries = (part["entries"] as? JsonArray)?.let(GoalJson::readEntries).orEmpty(),
                reminders = (part["reminders"] as? JsonArray)?.let(GoalJson::readReminders).orEmpty(),
                reviews = (part["reviews"] as? JsonArray)?.let(GoalJson::readReviews).orEmpty(),
            )
        }.getOrNull() ?: return "The goals in the backup couldn't be read."
        val includesNotes = part["includesNotes"]?.jsonPrimitive?.booleanOrNull ?: false
        return store.import(read, includesNotes, restore)
    }
}
