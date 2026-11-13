package com.accountabilibuddy.shared.sync

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalNewerLists
import com.accountabilibuddy.shared.goals.GoalReminder
import com.accountabilibuddy.shared.goals.GoalReviewRecord
import com.accountabilibuddy.shared.goals.GoalSyncClean
import com.accountabilibuddy.shared.goals.GoalSyncJson
import com.accountabilibuddy.shared.goals.GoalSyncMerge
import com.accountabilibuddy.shared.goals.GoalTombstones
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject

/**
 * The five goal parts of a sync file (ADR 79): `goals`, `goalAreas`, `goalEntries`, `goalReminders`, `goalReviews`. A null list is a part
 * the file does not carry (older app, QR file, goals not shared); [newer] holds each list's items of a newer app, kept as written.
 * The PC's twin is `SyncGoalParts` with the five `SyncFile` properties.
 */
data class SyncGoalParts(
    val goals: List<Goal>?,
    val areas: List<GoalArea>?,
    val entries: List<GoalEntry>?,
    val reminders: List<GoalReminder>?,
    val reviews: List<GoalReviewRecord>?,
    val newer: GoalNewerLists = GoalNewerLists.NONE,
) {
    companion object {
        /** The five names the parts have in a sync file. */
        val NAMES: List<String> = listOf("goals", "goalAreas", "goalEntries", "goalReminders", "goalReviews")

        /** [parts] carry [lists], cleaned: only usable items, no waiting change, tombstones without text (the file this device writes). */
        fun into(lists: GoalLists): SyncGoalParts {
            val outgoing = cleaned(lists)
            return SyncGoalParts(outgoing.goals, outgoing.areas, outgoing.entries, outgoing.reminders, outgoing.reviews, outgoing.newer)
        }

        /** The lists a file carries, cleaned; null when it carries no part at all (keep own goals). */
        fun from(parts: SyncGoalParts?): GoalLists? {
            if (parts == null || (parts.goals == null && parts.areas == null && parts.entries == null && parts.reminders == null && parts.reviews == null)) return null
            return cleaned(GoalLists(parts.areas ?: emptyList(), parts.goals ?: emptyList(), parts.entries ?: emptyList(), parts.reminders ?: emptyList(), parts.reviews ?: emptyList(), parts.newer))
        }

        /** What opening a file does to the parts (the PC's `WithCleanGoalParts`): every item the merge cannot use left out; a missing part stays missing. */
        fun cleanOpen(parts: SyncGoalParts?): SyncGoalParts? {
            if (parts == null) return null
            val clean = GoalSyncClean.lists(GoalLists(parts.areas.orEmpty(), parts.goals.orEmpty(), parts.entries.orEmpty(), parts.reminders.orEmpty(), parts.reviews.orEmpty(), parts.newer))
            return SyncGoalParts(
                if (parts.goals == null) null else clean.goals, if (parts.areas == null) null else clean.areas,
                if (parts.entries == null) null else clean.entries, if (parts.reminders == null) null else clean.reminders,
                if (parts.reviews == null) null else clean.reviews, clean.newer,
            )
        }

        // Only usable items; no waiting change anywhere; tombstones without text or unknown fields; the lists' newer items kept.
        private fun cleaned(given: GoalLists): GoalLists {
            val lists = GoalSyncClean.lists(given)
            return lists.copy(
                areas = lists.areas.map(GoalTombstones::clean),
                goals = lists.goals.map { GoalTombstones.clean(GoalSyncMerge.withoutPending(it)) },
                entries = lists.entries.map(GoalTombstones::clean),
                reminders = lists.reminders.map(GoalTombstones::clean),
                reviews = lists.reviews.map(GoalTombstones::clean),
            )
        }

        /** Reads the five parts of a file leniently (a part that is not a list reads as missing); null when it carries none. */
        fun read(json: JsonObject): SyncGoalParts? {
            val goals = GoalSyncJson.readGoals(present(json["goals"]))
            val areas = GoalSyncJson.readAreas(present(json["goalAreas"]))
            val entries = GoalSyncJson.readEntries(present(json["goalEntries"]))
            val reminders = GoalSyncJson.readReminders(present(json["goalReminders"]))
            val reviews = GoalSyncJson.readReviews(present(json["goalReviews"]))
            if (goals == null && areas == null && entries == null && reminders == null && reviews == null) return null
            return SyncGoalParts(
                goals?.known, areas?.known, entries?.known, reminders?.known, reviews?.known,
                GoalNewerLists(
                    areas = areas?.newer.orEmpty(), goals = goals?.newer.orEmpty(), entries = entries?.newer.orEmpty(),
                    reminders = reminders?.newer.orEmpty(), reviews = reviews?.newer.orEmpty(),
                ),
            )
        }

        /** The parts as the file writes them, in the PC's order; a part the file does not carry is left out. */
        fun write(parts: SyncGoalParts): List<Pair<String, JsonElement>> = buildList {
            parts.goals?.let { add("goals" to GoalSyncJson.writeGoals(GoalSyncJson.Part(it, parts.newer.goals))) }
            parts.areas?.let { add("goalAreas" to GoalSyncJson.writeAreas(GoalSyncJson.Part(it, parts.newer.areas))) }
            parts.entries?.let { add("goalEntries" to GoalSyncJson.writeEntries(GoalSyncJson.Part(it, parts.newer.entries))) }
            parts.reminders?.let { add("goalReminders" to GoalSyncJson.writeReminders(GoalSyncJson.Part(it, parts.newer.reminders))) }
            parts.reviews?.let { add("goalReviews" to GoalSyncJson.writeReviews(GoalSyncJson.Part(it, parts.newer.reviews))) }
        }

        private fun present(value: JsonElement?): JsonElement? = if (value is JsonNull) null else value
    }
}
