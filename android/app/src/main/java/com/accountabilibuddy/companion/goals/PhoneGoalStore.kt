package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.shared.goals.GoalOrderRules
import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalApplyResult
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalDefaults
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalListRules
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklySync
import com.accountabilibuddy.shared.goals.GoalDerivedCover
import com.accountabilibuddy.shared.goals.GoalDerivedReading
import com.accountabilibuddy.shared.goals.GoalDerivedWriter
import com.accountabilibuddy.shared.goals.GoalPhoneSync
import com.accountabilibuddy.shared.goals.GoalStoreRules
import com.accountabilibuddy.shared.goals.GoalTombstones
import com.accountabilibuddy.shared.sync.FieldStamps
import com.accountabilibuddy.shared.sync.SyncGoalParts
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalPauseStretch
import com.accountabilibuddy.shared.goals.GoalReminder
import com.accountabilibuddy.shared.goals.GoalReviewRecord
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
 *   ADR 79 forward compatibility: lists written by a newer version are not unreadable. An item holding a value this version doesn't
 *   know is kept as written ([GoalLists.newer]), unknown fields stay on their objects ([GoalJson]), both written back unchanged on
 *   every save, and no change removes them ([GoalUnknowns.keep] through [GoalListRules.tidy]).
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
    val deepWork: PhoneDeepWorkWeekly = PhoneDeepWorkWeekly(context).also { weekly ->
        weekly.recordStamp = {
            GoalPhoneSync.deepWorkOf(_lists.value)?.let(GoalPhoneSync::recordStamp)
                ?: com.accountabilibuddy.shared.sync.FieldStamp(com.accountabilibuddy.shared.sync.DotNetTime.MIN, com.accountabilibuddy.shared.sync.FieldStamp.NOBODY)
        }
    }

    /** NG1: Deep work's daily target when no PC is in the group (this phone's own, never sent; the PC's wins once one joins). */
    val deepWorkDaily: PhoneDeepWorkDaily = PhoneDeepWorkDaily(context).also { daily -> daily.environment = { deepWork.environment() } }

    /** This device's id as lower-case text (the empty id before the phone is in a group): the device a goal part changed here is stamped with. Set by the app. */
    var selfId: () -> String = { com.accountabilibuddy.shared.sync.FieldStamp.NOBODY }
        set(value) {
            field = value
            deepWork.selfId = value
        }

    /** Why a goals restore is refused right now (a focus session the user started, outside ProductaBuddy mode); null when allowed. Set by the app. */
    var restoreRefusedReason: () -> String? = { null }

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

    /** Changes several live areas (a reorder) in one saved change; any that is gone refuses the whole change. */
    fun updateAreas(changed: List<GoalArea>): String? = change { lists, now ->
        if (changed.isEmpty()) return@change Unchanged
        val byId = changed.associateBy { it.id }
        if (byId.keys.any { id -> lists.areas.none { it.id == id && it.isLive } }) return@change Refused(GONE)
        Changed(lists.copy(areas = lists.areas.map { old -> byId[old.id]?.copy(editedAt = GoalListRules.after(now, old.editedAt), deletedAt = null) ?: old }))
    }

    /** Deletes an area that holds no live goal (a tombstone with no text); one that still holds goals is refused. */
    fun deleteArea(id: UUID): String? = change { lists, now ->
        val old = lists.areas.firstOrNull { it.id == id && it.isLive } ?: return@change Refused(GONE)
        if (lists.goals.any { it.isLive && it.areaId == id }) return@change Refused("Move its goals to another area first.")
        val stamp = GoalListRules.after(now, old.editedAt)
        Changed(lists.copy(areas = lists.areas.map { if (it.id == id) it.copy(name = "", icon = "", color = "", editedAt = stamp, deletedAt = stamp) else it }))
    }

    /**
     * Deletes an area after moving its live goals to [heir], another live area, in one commit (brief §4.3: "Delete asks first and
     * says where its goals go"; the PC moves them the same way). The goals keep their order, after the heir's own, and all their
     * history; only their area and place change.
     */
    fun deleteAreaMovingGoals(id: UUID, heir: UUID): String? = change { lists, now ->
        val old = lists.areas.firstOrNull { it.id == id && it.isLive } ?: return@change Refused(GONE)
        if (heir == id || lists.areas.none { it.id == heir && it.isLive }) return@change Refused("Pick an area for its goals.")
        val moved = GoalOrderRules.movedTo(lists.goals, id, heir).associateBy { it.id }
        val goals = lists.goals.map { goal ->
            moved[goal.id]?.let { goal.copy(areaId = it.areaId, position = it.position, editedAt = GoalListRules.after(now, goal.editedAt)) } ?: goal
        }
        val stamp = GoalListRules.after(now, old.editedAt)
        Changed(lists.copy(goals = goals, areas = lists.areas.map { if (it.id == id) it.copy(name = "", icon = "", color = "", editedAt = stamp, deletedAt = stamp) else it }))
    }

    // ---------------------------------------------------------------- goals

    /**
     * Saves the new places of the goals that moved inside their area ([GoalOrderRules.reordered]) in one commit. Only [Goal.position]
     * is taken from [changed]: nothing else about a goal changes here, so it needs none of [updateGoal]'s checks.
     */
    fun reorderGoals(changed: List<Goal>): String? = change { lists, now ->
        val byId = changed.associateBy { it.id }
        if (byId.keys.any { id -> lists.goals.none { it.id == id && it.isLive } }) return@change Refused(GONE)
        Changed(lists.copy(goals = lists.goals.map { old -> byId[old.id]?.let { old.copy(position = it.position, editedAt = GoalListRules.after(now, old.editedAt)) } ?: old }))
    }

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

    /**
     * Deletes a goal (a tombstone with no text, [GoalTombstones.delete]). Its entries and reminders are left as they are (ADR 79: the
     * deletion travels, the counting and the reminders skip a goal that is not live). Deep work can't be deleted.
     */
    fun deleteGoal(id: UUID): String? = change { lists, now ->
        val old = lists.goals.firstOrNull { it.id == id && it.isLive } ?: return@change Refused(GONE)
        if (old.isDeepWork) return@change Refused("Deep work is built in: it can't be deleted.")
        val stamp = GoalListRules.after(now, old.editedAt)
        Changed(lists.copy(goals = lists.goals.map { if (it.id == id) GoalTombstones.delete(it, stamp) else it }))
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

    // ---------------------------------------------------------------- reminders and reviews (round 2 R6b3)

    /**
     * Sets [goalId]'s reminders to [wanted] (Edit goal › Reminders' Save): a reminder no longer there becomes a tombstone with no reason;
     * a new or changed one is stamped now (a reminder never rings for a moment before it was set); an unchanged one is left as it was,
     * so saving a goal later never re-arms it (the PC's milestone review S1). Suggestions only: they feed no guard.
     */
    fun setReminders(goalId: UUID, wanted: List<GoalReminder>): String? = change { lists, now ->
        if (lists.goals.none { it.id == goalId && it.isLive }) return@change Refused(GONE)
        val byId = wanted.filter { it.goalId == goalId }.associateBy { it.id }
        var changed = false
        val kept = lists.reminders.map { old ->
            if (old.goalId != goalId || !old.isLive) return@map old
            val next = byId[old.id]
            when {
                next == null -> {
                    changed = true
                    val stamp = GoalListRules.after(now, old.editedAt)
                    old.copy(reason = null, editedAt = stamp, deletedAt = stamp)
                }
                // Unknown fields (ADR 79) aren't on the editor's copy; they stay on the stored one either way.
                next.copy(editedAt = old.editedAt, deletedAt = null, extra = old.extra) == old -> old
                else -> {
                    changed = true
                    next.copy(editedAt = GoalListRules.after(now, old.editedAt), deletedAt = null)
                }
            }
        }
        val known = lists.reminders.map { it.id }.toSet()
        val added = wanted.filter { it.goalId == goalId && it.id !in known }.map { it.copy(editedAt = now, deletedAt = null) }
        if (!changed && added.isEmpty()) Unchanged else Changed(lists.copy(reminders = kept + added))
    }

    /**
     * Keeps a weekly review record (an answer or a snooze), stamped now; review notes stay private text. Each new or changed answer carries
     * its own time (decidedAt, ADR 79), one tick after the held answer for that goal, so two devices' answers for a week merge goal by goal.
     */
    fun saveReview(record: GoalReviewRecord): String? = change { lists, now ->
        val old = lists.reviews.firstOrNull { it.id == record.id }
        val stamp = old?.let { GoalListRules.after(now, it.editedAt) } ?: now
        val decisions = record.decisions.map { decision ->
            val held = old?.decisions?.firstOrNull { it.goalId == decision.goalId }
            if (held != null && held.choice == decision.choice && held.decidedAt != null) held
            else decision.copy(decidedAt = FieldStamps.next(now, held?.decidedAt))
        }
        val saved = record.copy(decisions = decisions, editedAt = stamp, deletedAt = null)
        Changed(lists.copy(reviews = if (old == null) lists.reviews + saved else lists.reviews.map { if (it.id == record.id) saved else it }))
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
        restoreRefusedReason()?.let { return "Goals weren't restored: $it" }
        val by = selfId()
        // ADR 79: a restore is an edit made here and syncs out: Replace stamps every part of the backup's live goals now, a merge the parts
        // it changed; the result is taken as stamped (Kind.Stored).
        val refused = change(GoalStoreRules.Kind.Stored) { lists, now -> Changed(GoalListRules.imported(lists, incoming, restore, includesNotes, now, by)) }
        val count = GoalListRules.importedCount(incoming)
        return when {
            refused != null -> "Goals weren't restored: $refused"
            restore -> "Goals restored ($count from the backup)."
            else -> "Goals merged ($count in the backup; the newest edit of each kept)."
        }
    }

    // ---------------------------------------------------------------- sync (ADR 79, GS4)

    /**
     * The five goal parts of this phone's own file ([GoalPhoneSync.outgoing]); null while the goals are not ready (no part, never an empty
     * list). Never a waiting change; Deep work's record carries the weekly target in effect with its own stamp.
     */
    fun partsForSync(now: OffsetDateTime): SyncGoalParts? =
        GoalPhoneSync.outgoing(_lists.value, deepWork.current(), _status.value == PhoneGoalStatus.Ready, now)

    /** This phone's lists as a sync file carries them (Deep work composed, no 90-day window): what history rows merge into. Null while not ready. */
    fun composed(): GoalLists? =
        if (_status.value == PhoneGoalStatus.Ready) GoalPhoneSync.composed(_lists.value, deepWork.current()) else null

    /**
     * Another device's goal parts taken in, merged item by item and goal part by goal part and taken as merged (no stamp again, nothing
     * brought back). The held lists are read and merged INSIDE this lock, right before the result is stored. Deep work's received weekly
     * target goes through this phone's looser-change rule. Refused (false) unless the goals are Ready. True when anything changed.
     */
    fun applySynced(incoming: SyncGoalParts?, pcDailyMinutes: Double?): Boolean =
        applyMerged { now -> GoalPhoneSync.applied(_lists.value, deepWork.current(), incoming, true, now, weeklyEnv(), pcDailyMinutes) } == GoalApplyResult.Changed

    /**
     * As [applySynced] for a merge the caller makes from the held (composed) lists, e.g. history rows; null from [merge] = nothing to take in.
     * [GoalApplyResult.Failed] when the goals aren't ready or the write failed (the caller then does not record its files as read).
     */
    fun applyHistory(pcDailyMinutes: Double?, merge: (GoalLists) -> GoalLists?): GoalApplyResult =
        applyMerged { _ ->
            val held = composed()
            held?.let(merge)?.let { GoalPhoneSync.afterMerge(_lists.value, deepWork.current(), it, weeklyEnv(), pcDailyMinutes) }
        }

    // Lock order: this store, then Deep work's weekly state (never the other way round). The weekly state is worked out from what is
    // held and adopted under Deep work's lock together, so a raise made here meanwhile (600 -> 900) is not overwritten by an older 600.
    @Synchronized
    private fun applyMerged(decide: (OffsetDateTime) -> GoalPhoneSync.Applied?): GoalApplyResult {
        if (_status.value != PhoneGoalStatus.Ready) return GoalApplyResult.Failed
        val now = clock()
        var listsChanged = false
        var weeklyChanged = false
        val result = synchronized(deepWork) {
            val applied = decide(now) ?: return@synchronized GoalApplyResult.Unchanged
            val before = _lists.value
            val after = GoalStoreRules.normalize(before, applied.lists, now, GoalStoreRules.Kind.Synced, selfId())
            listsChanged = after != before
            if (listsChanged) {
                if (!write(after)) return@synchronized GoalApplyResult.of(writeFailed = true, listsChanged = false, weeklyChanged = false)
                _lists.value = after
            }

            weeklyChanged = deepWork.adopt(applied.weekly)
            GoalApplyResult.of(writeFailed = false, listsChanged = listsChanged, weeklyChanged = weeklyChanged)
        }

        // Outside Deep work's lock (the hook starts other work), still inside this store's.
        if (listsChanged && result != GoalApplyResult.Failed) onChanged()
        return result
    }

    private fun weeklyEnv(): GoalDeepWorkWeeklySync.Env = deepWork.environment().toSync()

    /**
     * This phone's own derived entries made to match [readings] ([GoalDerivedWriter.changes]): written only when a value changed, never
     * another device's entry, never a hand entry. [covered]: the days the read covered. True when anything was written.
     */
    @Synchronized
    fun writeDerived(deviceId: UUID, readings: List<GoalDerivedReading>, covered: List<GoalDerivedCover>): Boolean {
        if (_status.value != PhoneGoalStatus.Ready) return false
        val now = clock()
        val changes = GoalDerivedWriter.changes(_lists.value.entries, deviceId, readings, covered, now)
        if (changes.isEmpty()) return false
        val next = GoalStoreRules.normalize(_lists.value, GoalDerivedWriter.applied(_lists.value, changes), now, GoalStoreRules.Kind.Local, selfId())
        if (next == _lists.value || !write(next)) return false
        _lists.value = next
        onChanged()
        return true
    }

    // ---------------------------------------------------------------- inside

    private sealed interface Outcome
    private data class Changed(val lists: GoalLists) : Outcome
    private data class Refused(val reason: String) : Outcome
    private data object Unchanged : Outcome

    // Runs one change on the current lists: refused unless Ready; the result is tidied ([GoalListRules.tidy]) and written in one commit.
    @Synchronized
    private fun change(kind: GoalStoreRules.Kind = GoalStoreRules.Kind.Local, edit: (GoalLists, OffsetDateTime) -> Outcome): String? {
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
                // ADR 79: every goal part the change altered is stamped by this device (GoalEdits), entry tombstones are kept for good.
                val tidied = GoalStoreRules.normalize(_lists.value, outcome.lists, now, kind, selfId())
                if (tidied == _lists.value) return null
                if (!write(tidied)) return "Goals couldn't be saved on this phone."
                _lists.value = tidied
                onChanged()
                null
            }
        }
    }

    private fun sourceProblem(goal: Goal): String? =
        if (goal.sources.all { GoalSources.allowedFor(goal, it.type) }) null else "One of its sources can't count on a goal yet."

    // Each list with its newer items back at their places (ADR 79).
    private fun write(lists: GoalLists): Boolean = runCatching {
        prefs.edit()
            .putString(KEY_AREAS, TokenVault.seal(GoalJson.writeAreas(lists.areas, lists.newer.areas).toString(), ALIAS))
            .putString(KEY_GOALS, TokenVault.seal(GoalJson.writeGoals(lists.goals, lists.newer.goals).toString(), ALIAS))
            .putString(KEY_ENTRIES, TokenVault.seal(GoalJson.writeEntries(lists.entries, lists.newer.entries).toString(), ALIAS))
            .putString(KEY_REMINDERS, TokenVault.seal(GoalJson.writeReminders(lists.reminders, lists.newer.reminders).toString(), ALIAS))
            .putString(KEY_REVIEWS, TokenVault.seal(GoalJson.writeReviews(lists.reviews, lists.newer.reviews).toString(), ALIAS))
            .putInt(KEY_MODEL, MODEL_VERSION)
            .commit()
    }.getOrDefault(false)

    // Nothing stored: not set up. The marker and all five lists, each opened and read: ready (an item of a newer version kept as
    // written, ADR 79). Anything else: unreadable.
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
            PhoneGoalStatus.Ready to GoalJson.readStoredLists(
                areas = array(KEY_AREAS),
                goals = array(KEY_GOALS),
                entries = array(KEY_ENTRIES),
                reminders = array(KEY_REMINDERS),
                reviews = array(KEY_REVIEWS),
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
            // ADR 70: all time comes from the trusted clock, never the phone's own.
            val now = OffsetDateTime.ofInstant(com.accountabilibuddy.companion.guidance.TrustedTime.now(), java.time.ZoneId.systemDefault())
            return now.withNano(now.nano / 100 * 100)
        }
    }
}
