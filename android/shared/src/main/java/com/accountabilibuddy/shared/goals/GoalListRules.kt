package com.accountabilibuddy.shared.goals

import java.time.Duration
import java.time.LocalDate
import java.time.OffsetDateTime
import java.util.UUID

/**
 * The goals store's pure rules, shared so the phone's store follows the PC's `GoalStore` / `GoalsService` and can be checked without a
 * device (android/checks): the edit stamps (Q423), the tidy-up after every change, and taking a backup's goals in (Q387, Q421).
 */
object GoalListRules {
    /** .NET's tick (100 ns): the smallest step a stamp moves by on the PC. */
    const val TICK_NANOS = 100L

    /** Tombstones are kept this long, then dropped (ADR 48/72's rule). */
    val TOMBSTONES_KEPT: Duration = Duration.ofDays(30)

    /** A stamp that outranks [old] even when the clock hasn't moved past it: [now], or one tick after [old]. */
    fun after(now: OffsetDateTime, old: OffsetDateTime): OffsetDateTime =
        if (now.toInstant().isAfter(old.toInstant())) now else OffsetDateTime.ofInstant(old.toInstant().plusNanos(TICK_NANOS), now.offset)

    /**
     * A new hand entry's stamp (Q423, the PC's `GoalsService.SaveEntryAsync`): [now], or one tick after the newest hand entry of that
     * goal and day when the clock hasn't moved past it, so "your latest decision wins" never falls to the id tie-break.
     */
    fun handEntryStamp(entries: List<GoalEntry>, goalId: UUID, day: LocalDate, now: OffsetDateTime): OffsetDateTime {
        val newest = entries.filter { it.goalId == goalId && it.day == day && it.source == GoalEntrySource.Hand }.maxByOrNull { it.editedAt.toInstant() }
        return if (newest == null) now else after(now, newest.editedAt)
    }

    /**
     * After every change, the PC's `GoalStore.Normalize` rule for rule: Deep work kept whole ([GoalDefaults.normalized]) and first, no
     * other goal built in; a live goal whose area isn't live goes to "Work", made again if needed, stamped from the lists, never the
     * clock ([GoalAreaTidy], GS0: the PC's rule, no longer "the first live area" unstamped); tombstones older than [TOMBSTONES_KEPT]
     * are dropped. What this version doesn't know comes back from [before] (the lists before the change) when the change left it out
     * ([GoalUnknowns.keep], ADR 79).
     */
    fun tidy(lists: GoalLists, now: OffsetDateTime, before: GoalLists = lists): GoalLists {
        val keptFrom = now.toInstant().minus(TOMBSTONES_KEPT)
        // The PC keeps a tombstone deleted exactly at the cutoff (at >= cutoff).
        fun kept(deletedAt: OffsetDateTime?) = deletedAt == null || !deletedAt.toInstant().isBefore(keptFrom)
        val others = lists.goals.filter { it.id != Goal.DEEP_WORK_ID }.map { if (it.builtIn) it.copy(builtIn = false) else it }
        val deepWork = lists.goals.firstOrNull { it.id == Goal.DEEP_WORK_ID } ?: before.goals.firstOrNull { it.id == Goal.DEEP_WORK_ID }
        val goals = if (deepWork == null) others else listOf(GoalDefaults.normalized(deepWork)) + others
        val (areas, placed) = GoalAreaTidy.rehome(lists.areas, goals, GoalDefaults.newWorkArea(now))
        val tidied = lists.copy(
            areas = areas.filter { kept(it.deletedAt) },
            goals = placed.filter { kept(it.deletedAt) },
            entries = lists.entries.filter { kept(it.deletedAt) },
            reminders = lists.reminders.filter { kept(it.deletedAt) },
            reviews = lists.reviews.filter { kept(it.deletedAt) },
        )
        return GoalUnknowns.keep(before, tidied)
    }

    /** How many goals a backup brings, as the restore summary counts them: its live goals but Deep work. */
    fun importedCount(backup: GoalLists): Int = backup.goals.count { it.isLive && !it.isDeepWork }

    /**
     * A backup's goals taken in (the PC's `GoalStore.Imported`, rule for rule): areas first; other goals with only the sources allowed
     * now and no waiting change or second target (they feed no guard); Deep work takes only name, icon, colour, Show on Today, extra
     * tiles and notes (its place, sources and targets stay this device's); entries and reminders only for goals live after the merge,
     * never an entry for Deep work. [replace]: the backup's live items are stamped [now] and win; otherwise the newest edit wins (a tie
     * keeps this device's). Tombstones in the backup are ignored (an old backup never deletes). Without [includesNotes] the device's own
     * notes, reasons and review notes stay. Run [tidy] after (a goal with no live area goes to "Work").
     */
    fun imported(lists: GoalLists, backup: GoalLists, replace: Boolean, includesNotes: Boolean, now: OffsetDateTime): GoalLists {
        fun stamp(edited: OffsetDateTime) = if (replace) now else edited

        val areas = merge(lists.areas, backup.areas, { it.id }, { it.isLive }, { it.editedAt }, replace) { incoming, _ -> incoming.copy(editedAt = stamp(incoming.editedAt)) }

        val ownDeepWork = lists.goals.firstOrNull { it.id == Goal.DEEP_WORK_ID }
        val incomingGoals = backup.goals.filter { !it.isDeepWork }.map { goal ->
            goal.copy(
                sources = goal.sources.filter { GoalSources.allowedFor(goal, it.type) },
                secondTarget = null,
                target = goal.target.copy(pendingChange = null),
            )
        }
        val goals = merge(lists.goals, incomingGoals, { it.id }, { it.isLive }, { it.editedAt }, replace) { incoming, held ->
            incoming.copy(editedAt = stamp(incoming.editedAt), notes = if (includesNotes) incoming.notes else held?.notes)
        }.toMutableList()
        val backupDeepWork = backup.goals.firstOrNull { it.id == Goal.DEEP_WORK_ID && it.isLive }
        if (ownDeepWork != null && backupDeepWork != null && (replace || backupDeepWork.editedAt.toInstant().isAfter(ownDeepWork.editedAt.toInstant()))) {
            val index = goals.indexOfFirst { it.id == Goal.DEEP_WORK_ID }
            goals[index] = ownDeepWork.copy(
                name = backupDeepWork.name,
                icon = backupDeepWork.icon,
                color = backupDeepWork.color,
                showOnToday = backupDeepWork.showOnToday,
                extraTiles = backupDeepWork.extraTiles,
                notes = if (includesNotes) backupDeepWork.notes else ownDeepWork.notes,
                editedAt = now,
            )
        }

        val liveGoals = goals.filter { it.isLive }.map { it.id }.toSet()
        val entries = merge(
            lists.entries, backup.entries.filter { it.goalId != Goal.DEEP_WORK_ID && it.goalId in liveGoals },
            { it.id }, { it.isLive }, { it.editedAt }, replace,
        ) { incoming, _ -> incoming.copy(editedAt = stamp(incoming.editedAt)) }
        val reminders = merge(lists.reminders, backup.reminders.filter { it.goalId in liveGoals }, { it.id }, { it.isLive }, { it.editedAt }, replace) { incoming, held ->
            incoming.copy(editedAt = stamp(incoming.editedAt), reason = if (includesNotes) incoming.reason else held?.reason)
        }
        val reviews = merge(lists.reviews, backup.reviews, { it.id }, { it.isLive }, { it.editedAt }, replace) { incoming, held ->
            incoming.copy(editedAt = stamp(incoming.editedAt), notes = if (includesNotes) incoming.notes else held?.notes)
        }
        // copy: this device's newer items (ADR 79) stay; the backup's are never taken.
        return lists.copy(areas = areas, goals = goals, entries = entries, reminders = reminders, reviews = reviews)
    }

    // The backup's live items come in (the first of each id); with replace they always win, otherwise only over an older edit. The
    // device's other items stay.
    private fun <T> merge(
        current: List<T>,
        backup: List<T>,
        id: (T) -> UUID,
        live: (T) -> Boolean,
        editedAt: (T) -> OffsetDateTime,
        replace: Boolean,
        take: (T, T?) -> T,
    ): List<T> {
        val merged = current.toMutableList()
        for (incoming in backup.filter(live).distinctBy(id)) {
            val index = merged.indexOfFirst { id(it) == id(incoming) }
            val held = if (index < 0) null else merged[index]
            if (held != null && !replace && !editedAt(incoming).toInstant().isAfter(editedAt(held).toInstant())) {
                continue
            }

            if (index < 0) merged += take(incoming, null) else merged[index] = take(incoming, held)
        }

        return merged
    }
}
