package com.accountabilibuddy.companion.goals

import android.content.Context
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.shared.goals.GoalDeepWorkTargets
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklySync
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalPendingChange
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.Duration
import java.time.OffsetDateTime
import java.time.ZoneId
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Deep work's weekly target on this phone (plan §3b 6; the PC's `goals.deepWork` document), in the plain preferences "goalsDeepWork":
 * minutes, "Track time only" as the PC set it, a waiting change and the stamps it travels under; no text. Its daily target is the PC's, read
 * only (ADR 29). The looser-change rule is [GoalDeepWorkWeeklySync] (on [com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyRules]):
 * raising applies at once; lowering or removing waits the delay, is refused during a session (outside ProductaBuddy mode) and never applies
 * while one runs; there is no "apply now". A waiting change is settled whenever it is read, so it also holds across a restart; nothing here
 * is read by a guard, the goal unlock or `focusDay`.
 *
 * ADR 79 (GS4): the weekly target in effect goes out in Deep work's record under its own stamp ([forSync]); another device's goes through the
 * same rule as a change made here ([GoalPhoneSync][com.accountabilibuddy.shared.goals.GoalPhoneSync], [adopt]): higher or new at once, lower
 * or removed waits THIS phone's delay from the receipt, held while a session runs, landing with the sender's stamp. Cancel of such a change
 * stamps this phone's value again.
 */
class PhoneDeepWorkWeekly(context: Context) {
    /**
     * The facts the rule needs, read when a change is asked or settled (set by the app).
     *
     * @property sessionRunning A focus session the user started runs (the check the removal lock uses).
     * @property lowerWaitsNow The work-hours rule holds a lowering: the phone has no such rule, so false (Q504).
     */
    data class Environment(
        val productaBuddy: Boolean,
        val sessionRunning: Boolean,
        val now: OffsetDateTime,
        val looserDelay: Duration,
        val lowerWaitsNow: Boolean = false,
    )

    private val prefs = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
    private val _state = MutableStateFlow(load())

    /** The stored state as last read or written (the screens read [current], which settles first). */
    val state: StateFlow<GoalDeepWorkWeekly> = _state.asStateFlow()

    /** Set by the app; the default is a quiet AccountabiliBuddy phone with no session (never used once the app is up), on the trusted clock. */
    var environment: () -> Environment = {
        Environment(false, false, OffsetDateTime.ofInstant(TrustedTime.now(), ZoneId.systemDefault()), Duration.ofMinutes(10))
    }

    /** Set by the app: the weekly target or its waiting change was written (an auto-backup soon). */
    var onChanged: () -> Unit = {}

    /** Set by the store: the stored Deep work record's own weekly-part stamp (what counts while this phone holds none of its own). */
    var recordStamp: () -> FieldStamp = { FieldStamp(DotNetTime.MIN, FieldStamp.NOBODY) }

    /** Set by the app: this device's id as lower-case text (the empty id before the phone is in a group), the device a change made here is stamped with. */
    var selfId: () -> String = { FieldStamp.NOBODY }

    /** Set by the app: the PC's daily target in minutes (what "Track time only" would take away); null when none has arrived. */
    var pcDailyMinutes: () -> Double? = { null }

    /** The weekly target now: a waiting change whose time has come, with no session running, applies first. */
    @Synchronized
    fun current(): GoalDeepWorkWeekly {
        val settled = GoalDeepWorkWeeklySync.settle(_state.value, recordStamp(), env(), selfId())
        if (settled != _state.value) save(settled)
        return _state.value
    }

    /** Asks for [minutes] (null = none); what happened. */
    @Synchronized
    fun set(minutes: Int?): GoalDeepWorkWeeklyResult {
        val change = GoalDeepWorkWeeklySync.setLocal(current(), minutes, recordStamp(), env(), selfId())
        if (change.state != _state.value) save(change.state)
        return change.result
    }

    /** Cancel on a waiting change (the goal page, Edit goal). A change received from another device is stamped over, so it takes this value back. */
    @Synchronized
    fun cancel() {
        val cancelled = GoalDeepWorkWeeklySync.cancel(_state.value, recordStamp(), env().now, selfId())
        if (cancelled != _state.value) save(cancelled)
    }

    /**
     * A backup's weekly target and "Track time only", through the same rules as a change made here (plan §3b 6): a lower weekly target, none or
     * Track time only turning on waits (or is refused during a session); a raise and Track time only turning off apply at once. [trackTimeOnly]
     * is null in a backup that doesn't say (this phone's stays unless the weekly target restored ends it).
     */
    @Synchronized
    fun restore(backupMinutes: Int?, trackTimeOnly: Boolean? = null): GoalDeepWorkWeeklyResult {
        val results = ArrayList<GoalDeepWorkWeeklyResult>()
        if (trackTimeOnly == true) {
            val change = GoalDeepWorkWeeklySync.setTrackTimeOnlyLocal(current(), true, pcDailyMinutes(), recordStamp(), env(), selfId())
            if (change.state != _state.value) save(change.state)
            return change.result
        }

        if (trackTimeOnly == false) {
            val change = GoalDeepWorkWeeklySync.setTrackTimeOnlyLocal(current(), false, pcDailyMinutes(), recordStamp(), env(), selfId())
            if (change.state != _state.value) save(change.state)
            results.add(change.result)
        }

        val change = GoalDeepWorkWeeklySync.setLocal(current(), backupMinutes, recordStamp(), env(), selfId())
        if (change.state != _state.value) save(change.state)
        results.add(change.result)
        return results.firstOrNull { it == GoalDeepWorkWeeklyResult.RefusedDuringSession }
            ?: results.firstOrNull { it == GoalDeepWorkWeeklyResult.Waiting }
            ?: change.result
    }

    /** What Deep work's record carries out: the weekly target in effect (never a waiting one) and Track time only, with their own stamp. */
    @Synchronized
    fun forSync(): GoalDeepWorkTargets = GoalDeepWorkWeeklySync.targets(current(), recordStamp())

    /** Another device's values taken in ([com.accountabilibuddy.shared.goals.GoalPhoneSync.afterMerge] decided them): kept as they are. */
    @Synchronized
    fun adopt(next: GoalDeepWorkWeekly) {
        if (next != _state.value) save(next)
    }

    private fun env() = environment().let { GoalDeepWorkWeeklySync.Env(it.productaBuddy, it.sessionRunning, it.now, it.looserDelay, it.lowerWaitsNow) }

    private fun save(state: GoalDeepWorkWeekly) {
        _state.value = state
        val editor = prefs.edit()
            .putInt(MINUTES, state.minutes ?: NONE)
            .putBoolean(TRACK_TIME_ONLY, state.trackTimeOnly)
            .putBoolean(PENDING_AFTER_SESSION, state.pendingAfterSession)
        put(editor, "", state.pending)
        put(editor, TTO, state.pendingTrackTimeOnly)
        stamp(editor, STAMP, state.stamp)
        stamp(editor, PENDING_STAMP, state.pendingStamp)
        editor.apply()
        onChanged()
    }

    private fun put(editor: android.content.SharedPreferences.Editor, prefix: String, pending: GoalPendingChange?) {
        editor.putBoolean(PENDING + prefix, pending != null)
            .putInt(PENDING_MINUTES + prefix, pending?.amount?.toInt() ?: NONE)
            .putString(PENDING_AT + prefix, pending?.let { GoalJson.time(it.appliesAt) })
            .putBoolean(PENDING_PB + prefix, pending?.madeInProductaBuddy ?: false)
    }

    private fun stamp(editor: android.content.SharedPreferences.Editor, name: String, stamp: FieldStamp?) {
        editor.putString("$name.at", stamp?.let { GoalJson.time(it.at) }).putString("$name.by", stamp?.by)
    }

    private fun load(): GoalDeepWorkWeekly {
        val minutes = prefs.getInt(MINUTES, NONE).takeIf { it != NONE }
        return GoalDeepWorkWeekly(
            minutes = minutes,
            pending = pending(""),
            trackTimeOnly = prefs.getBoolean(TRACK_TIME_ONLY, false),
            pendingTrackTimeOnly = pending(TTO),
            stamp = stamp(STAMP),
            pendingStamp = stamp(PENDING_STAMP),
            pendingAfterSession = prefs.getBoolean(PENDING_AFTER_SESSION, false),
        )
    }

    private fun pending(prefix: String): GoalPendingChange? {
        if (!prefs.getBoolean(PENDING + prefix, false)) return null
        val at = prefs.getString(PENDING_AT + prefix, null)?.let { runCatching { GoalJson.parseTime(it) }.getOrNull() } ?: return null
        val amount = prefs.getInt(PENDING_MINUTES + prefix, NONE).takeIf { it != NONE }?.toDouble()
        return GoalPendingChange(amount, at, prefs.getBoolean(PENDING_PB + prefix, false))
    }

    private fun stamp(name: String): FieldStamp? {
        val at = prefs.getString("$name.at", null)?.let { runCatching { GoalJson.parseTime(it) }.getOrNull() } ?: return null
        return FieldStamp(at, prefs.getString("$name.by", null) ?: return null)
    }

    companion object {
        const val FILE = "goalsDeepWork"
        private const val NONE = -1
        private const val MINUTES = "weeklyMinutes"
        private const val PENDING = "pending"
        private const val PENDING_MINUTES = "pendingMinutes"
        private const val PENDING_AT = "pendingAt"
        private const val PENDING_PB = "pendingMadeInProductaBuddy"
        private const val TTO = "Tto"
        private const val TRACK_TIME_ONLY = "trackTimeOnly"
        private const val PENDING_AFTER_SESSION = "pendingAfterSession"
        private const val STAMP = "stamp"
        private const val PENDING_STAMP = "pendingStamp"
    }
}
