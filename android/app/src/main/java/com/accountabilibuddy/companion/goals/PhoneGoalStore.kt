package com.accountabilibuddy.companion.goals

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalDefaults
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalListRules
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalPauseStretch
import com.accountabilibuddy.shared.goals.GoalSources
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray

/**
 * The phone's goals (round 2 R6; the PC's `GoalStore`): areas, goals, entries, goal reminders and weekly reviews, device-local
 * until ADR 79 is accepted (nothing here is in a sync file). Each list is kept in the PC's JSON shape ([GoalJson]) and sealed with a
 * key in the Android Keystore ([TokenVault], its own alias), like the phone's task lists: goal and area names, notes, reminder
 * reasons, review notes, a source's #tag or app are never stored, logged or shown anywhere unsealed. The five lists and the marker
 * are written together in one preferences commit, so a list is never newer than another.
 *
 * - **Not set up** until the first run ([PhoneGoalsMigration]) made "Work" and Deep work; every change is refused until then.
 * - **Unreadable** when something stored can't be opened or read: never guessed at, rebuilt or written over; every change is refused.
 * - Items keep ids, positions, edit stamps and 30-day tombstones (no text), so syncing them later is a merge, not a migration.
 * - Deep work is kept whole on every change ([GoalDefaults.normalized]): built in, focus only, never paused, deleted or logged
 *   by hand; its daily target is the PC's (read only, ADR 29) and never stored here; its weekly target is [deepWork]'s, with the
 *   looser-change wait.
 * - Other goals feed no guard: their changes apply at once in both modes (brief §5.7).
 * - A hand entry is stamped just after every other hand entry of that goal and day (Q423), so "your latest decision wins" never
 *   falls to the id tie-break.
 *
 * Every change returns null when done, or the reason it was refused (for the screen).
 */
class PhoneGoalStore(context: Context, private val clock: () -> OffsetDateTime = ::stampNow) {
    private val prefs = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
    private val _status: MutableStateFlow<PhoneGoalStatus>
    private val _lists: MutableStateFlow<GoalLists>

    init {
        val (status, lists) = load()
        _status = MutableStateFlow(status)
        _lists = MutableStateFlow(lists)
    }

    /** Deep work's weekly target with its looser-change wait (plain, device-local; its daily target is the PC's). */
    val deepWork: PhoneDeepWorkWeekly = PhoneDeepWorkWeekly(context)

    /** Whether the goals can be used. */
    val status: StateFlow<PhoneGoalStatus> = _status.asStateFlow()

    /** The stored lists (tombstones included); empty unless [status] is Ready. */
    val lists: StateFlow<GoalLists> = _lists.asStateFlow()

    /** Set by the app: the goals changed here (an auto-backup soon). */
    var onChanged: () -> Unit = {}

    // ---------------------------------------------------------------- first run

    /**
     * The first run's lists ([PhoneGoalsMigration]): written with the marker in one commit, only while nothing is stored. False
     * (nothing written) when the goals are already set up or unreadable, or the write failed.
     */
    @Synchronized
    internal fun seed(lists: GoalLists): Boolean {
        if (_status.value != PhoneGoalStatus.NotSetUp) return false
        if (!write(lists)) return false
        _lists.value = lists
        _status.value = PhoneGoalStatus.Ready
        return true
    }

    // ---------------------------------------------------------------- areas

    /** Adds [area] (its id new); stamped now. */
    fun addArea(area: GoalArea): String? = change { lists, now ->
        if (lists.areas.any { it.id == area.id }) return@change Refused("That area already exists.")
        Changed(lists.copy(areas = lists.areas + area.copy(editedAt = now, deletedAt = null)))
    }

    /** Changes a live area's name, icon, colour or place. */
    fun updateArea(area: GoalArea): String? = change { lists, now ->
        val old = lists.areas.firstOrNull { it.id == area.id && it.isLive } ?: return@change Refused(GONE)
        Changed(lists.copy(areas = lists.areas.map { if (it.id == old.id) area.copy(editedAt = GoalListRules.after(now, old.editedAt), deletedAt = null) else it }))
    }

    /** Deletes an area that holds no live goal (a tombstone with no text); one that still holds goals is refused. */
    fun deleteArea(id: UUID): String? = change { lists, now ->
        val old = lists.areas.firstOrNull { it.id == id && it.isLive } ?: return@change Refused(GONE)
        if (lists.goals.any { it.isLive && it.areaId == id }) return@change Refused("Move its goals to another area first.")
        val stamp = GoalListRules.after(now, old.editedAt)
        Changed(lists.copy(areas = lists.areas.map { if (it.id == id) it.copy(name = "", icon = "", color = "", editedAt = stamp, deletedAt = stamp) else it }))
    }

    // ---------------------------------------------------------------- goals

    /** Adds [goal] (its id new) to a live area; a second built-in goal, Deep work's own source or a "Coming soon" source is refused. */
    fun addGoal(goal: Goal): String? = change { lists, now ->
        if (goal.isDeepWork || lists.goals.any { it.id == goal.id }) return@change Refused("That goal can't be added.")
        sourceProblem(goal)?.let { return@change Refused(it) }
        if (lists.areas.none { it.id == goal.areaId && it.isLive }) return@change Refused("Pick an area for it.")
        Changed(lists.copy(goals = lists.goals + goal.copy(editedAt = now, deletedAt = null, target = goal.target.copy(pendingChange = null), secondTarget = null)))
    }

    /**
     * Changes a live goal. Deep work takes only what the user may change on it (name, icon, colour, area, place, Show on Today, extra
     * tiles, notes); its targets and sources never change here. Another goal's change, a lower target included, applies at once.
     */
    fun updateGoal(goal: Goal): String? = change { lists, now ->
        val old = lists.goals.firstOrNull { it.id == goal.id && it.isLive } ?: return@change Refused(GONE)
        if (lists.areas.none { it.id == goal.areaId && it.isLive }) return@change Refused("Pick an area for it.")
        val updated = if (old.isDeepWork) {
            GoalDefaults.normalized(
                old.copy(
                    name = goal.name, icon = goal.icon, color = goal.color, areaId = goal.areaId, position = goal.position,
                    showOnToday = goal.showOnToday, extraTiles = goal.extraTiles, notes = goal.notes,
                ),
            )
        } else {
            if (goal.builtIn) return@change Refused("That goal can't be built in.")
            sourceProblem(goal)?.let { return@change Refused(it) }
            goal.copy(target = goal.target.copy(pendingChange = null), secondTarget = null, createdAt = old.createdAt)
        }
        Changed(lists.copy(goals = lists.goals.map { if (it.id == old.id) updated.copy(editedAt = GoalListRules.after(now, old.editedAt), deletedAt = null) else it }))
    }

    /** Deletes a goal with its entries and reminders (tombstones with no text). Deep work can't be deleted. */
    fun deleteGoal(id: UUID): String? = change { lists, now ->
        val old = lists.goals.firstOrNull { it.id == id && it.isLive } ?: return@change Refused(GONE)
        if (old.isDeepWork) return@change Refused("Deep work is built in: it can't be deleted.")
        val stamp = GoalListRules.after(now, old.editedAt)
        Changed(
            lists.copy(
                goals = lists.goals.map { if (it.id == id) it.copy(name = "", notes = null, sources = emptyList(), editedAt = stamp, deletedAt = stamp) else it },
                entries = lists.entries.map { if (it.goalId == id && it.isLive) it.copy(editedAt = GoalListRules.after(now, it.editedAt), deletedAt = GoalListRules.after(now, it.editedAt)) else it },
                reminders = lists.reminders.map { if (it.goalId == id && it.isLive) it.copy(reason = null, editedAt = GoalListRules.after(now, it.editedAt), deletedAt = GoalListRules.after(now, it.editedAt)) else it },
            ),
        )
    }

    /** Pauses a goal from [day] (at once; neither hit nor missed while paused). Deep work can't be paused. */
    fun pauseGoal(id: UUID, day: LocalDate): String? = change { lists, now ->
        val old = lists.goals.firstOrNull { it.id == id && it.isLive } ?: return@change Refused(GONE)
        if (old.isDeepWork) return@change Refused("Deep work is built in: it can't be paused.")
        if (old.isPausedOn(day)) return@change Unchanged
        val pauses = old.pauses.orEmpty() + GoalPauseStretch(day)
        Changed(lists.copy(goals = lists.goals.map { if (it.id == id) it.copy(pauses = pauses, editedAt = GoalListRules.after(now, it.editedAt)) else it }))
    }

    /** Turns a paused goal back on from [day] (that day counts again). */
    fun resumeGoal(id: UUID, day: LocalDate): String? = change { lists, now ->
        val old = lists.goals.firstOrNull { it.id == id && it.isLive } ?: return@change Refused(GONE)
        val open = old.pauses.orEmpty().any { it.resumedOn == null }
        if (!open) return@change Unchanged
        // A stretch that would end on or before its first day is dropped (paused and resumed the same day).
        val pauses = old.pauses.orEmpty().mapNotNull { stretch ->
            if (stretch.resumedOn != null) stretch else if (day > stretch.from) stretch.copy(resumedOn = day) else null
        }.ifEmpty { null }
        Changed(lists.copy(goals = lists.goals.map { if (it.id == id) it.copy(pauses = pauses, editedAt = GoalListRules.after(now, it.editedAt)) else it }))
    }

    // ---------------------------------------------------------------- entries

    /**
     * A hand entry for [goalId] on [day] (a tick is 1, an untick 0, a typed value, time added by hand): stamped now, or one tick
     * (100 ns) after the newest hand entry of that goal and day when the clock hasn't moved past it (Q423), so it is always "your
     * latest decision". Deep work takes no hand entry (focus only; hand-added time is a session added on the PC, ADR 20).
     */
    fun addHandEntry(goalId: UUID, day: LocalDate, value: Double, time: LocalTime? = null): String? = change { lists, now ->
        val goal = lists.goals.firstOrNull { it.id == goalId && it.isLive } ?: return@change Refused(GONE)
        if (goal.isDeepWork) return@change Refused("Deep work counts focus only.")
        val stamp = GoalListRules.handEntryStamp(lists.entries, goalId, day, now)
        val position = (lists.entries.filter { it.goalId == goalId && it.day == day }.maxOfOrNull { it.position } ?: 0.0) + 1.0
        val entry = GoalEntry(UUID.randomUUID(), goalId, day, time, value, GoalEntrySource.Hand, stamp, position)
        Changed(lists.copy(entries = lists.entries + entry))
    }

    // ---------------------------------------------------------------- backups

    /**
     * Takes a backup's goals in through the store's rules ([PhoneGoalsBackup]): areas first; no second built-in goal (Deep work takes
     * only name, icon, colour, Show on Today, extra tiles and notes); only sources allowed now; no waiting change or second target;
     * a goal with no live area goes to "Work"; entries and reminders only for goals live after the merge (no Deep work entry);
     * deletions never taken. [restore]: the backup's live items are stamped now and win; otherwise the newest edit of each wins.
     * Without [includesNotes] (a backup with no password) the phone's own notes, reasons and review notes are kept.
     * Returns a line for the restore summary.
     */
    fun import(incoming: GoalLists, includesNotes: Boolean, restore: Boolean): String {
        val refused = change { lists, now -> Changed(GoalListRules.imported(lists, incoming, restore, includesNotes, now)) }
        val count = GoalListRules.importedCount(incoming)
        return when {
            refused != null -> "Goals weren't restored: $refused"
            restore -> "Goals restored ($count from the backup)."
            else -> "Goals merged ($count in the backup; the newest edit of each kept)."
        }
    }

    // ---------------------------------------------------------------- inside

    private sealed interface Outcome
    private data class Changed(val lists: GoalLists) : Outcome
    private data class Refused(val reason: String) : Outcome
    private data object Unchanged : Outcome

    // Runs one change on the current lists: refused unless Ready; the result is tidied ([GoalListRules.tidy]) and written in one commit.
    @Synchronized
    private fun change(edit: (GoalLists, OffsetDateTime) -> Outcome): String? {
        when (_status.value) {
            PhoneGoalStatus.NotSetUp -> return "Goals are still being set up. Open the app again in a moment."
            PhoneGoalStatus.Unreadable -> return UNREADABLE
            PhoneGoalStatus.Ready -> Unit
        }
        val now = clock()
        return when (val outcome = edit(_lists.value, now)) {
            is Refused -> outcome.reason
            Unchanged -> null
            is Changed -> {
                val tidied = GoalListRules.tidy(outcome.lists, now)
                if (!write(tidied)) return "Goals couldn't be saved on this phone."
                _lists.value = tidied
                onChanged()
                null
            }
        }
    }

    private fun sourceProblem(goal: Goal): String? =
        if (goal.sources.all { GoalSources.allowedFor(goal, it.type) }) null else "One of its sources can't count on a goal yet."

    private fun write(lists: GoalLists): Boolean = runCatching {
        prefs.edit()
            .putString(KEY_AREAS, TokenVault.seal(GoalJson.writeAreas(lists.areas).toString(), ALIAS))
            .putString(KEY_GOALS, TokenVault.seal(GoalJson.writeGoals(lists.goals).toString(), ALIAS))
            .putString(KEY_ENTRIES, TokenVault.seal(GoalJson.writeEntries(lists.entries).toString(), ALIAS))
            .putString(KEY_REMINDERS, TokenVault.seal(GoalJson.writeReminders(lists.reminders).toString(), ALIAS))
            .putString(KEY_REVIEWS, TokenVault.seal(GoalJson.writeReviews(lists.reviews).toString(), ALIAS))
            .putInt(KEY_MODEL, MODEL_VERSION)
            .commit()
    }.getOrDefault(false)

    // Nothing stored: not set up. The marker and all five lists, each opened and read: ready. Anything else: unreadable.
    private fun load(): Pair<PhoneGoalStatus, GoalLists> {
        val model = prefs.getInt(KEY_MODEL, 0)
        val stored = LIST_KEYS.associateWith { prefs.getString(it, null) }
        if (model == 0 && stored.values.all { it == null }) return PhoneGoalStatus.NotSetUp to GoalLists.EMPTY
        if (model != MODEL_VERSION || stored.values.any { it == null }) return PhoneGoalStatus.Unreadable to GoalLists.EMPTY
        return runCatching {
            fun array(key: String): JsonArray {
                val text = TokenVault.open(stored.getValue(key)!!, ALIAS) ?: error("sealed list can't be opened")
                return Json.parseToJsonElement(text) as JsonArray
            }
            PhoneGoalStatus.Ready to GoalLists(
                areas = GoalJson.readAreas(array(KEY_AREAS)),
                goals = GoalJson.readGoals(array(KEY_GOALS)),
                entries = GoalJson.readEntries(array(KEY_ENTRIES)),
                reminders = GoalJson.readReminders(array(KEY_REMINDERS)),
                reviews = GoalJson.readReviews(array(KEY_REVIEWS)),
            )
        }.getOrElse { PhoneGoalStatus.Unreadable to GoalLists.EMPTY }
    }

    companion object {
        const val FILE = "goals"
        private const val ALIAS = "accountabilibuddy-goals"
        private const val KEY_MODEL = "goals.model"
        private const val KEY_AREAS = "goals.areas"
        private const val KEY_GOALS = "goals.items"
        private const val KEY_ENTRIES = "goals.entries"
        private const val KEY_REMINDERS = "goals.reminders"
        private const val KEY_REVIEWS = "goals.reviews"
        private val LIST_KEYS = listOf(KEY_AREAS, KEY_GOALS, KEY_ENTRIES, KEY_REMINDERS, KEY_REVIEWS)

        /** The stored model's version (the PC's `goals.model` = 1). */
        const val MODEL_VERSION = 1

        private const val GONE = "That goal or area isn't there any more."
        private const val UNREADABLE = "Your goals can't be read on this phone, so nothing is changed."

        /** Now in the phone's offset, cut to .NET's 100 ns ticks (what a stamp keeps on the PC). */
        fun stampNow(): OffsetDateTime {
            val now = OffsetDateTime.now()
            return now.withNano(now.nano / 100 * 100)
        }
    }
}
