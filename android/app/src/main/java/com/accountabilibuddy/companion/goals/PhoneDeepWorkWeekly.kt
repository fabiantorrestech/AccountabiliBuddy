package com.accountabilibuddy.companion.goals

import android.content.Context
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyRules
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalPendingChange
import java.time.Duration
import java.time.OffsetDateTime
import java.time.ZoneId
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Deep work's weekly target on this phone (plan §3b 6; the PC's `goals.deepWork` document), in the plain preferences "goalsDeepWork":
 * minutes and a waiting change, no text, never synced (its daily target is the PC's, read only: ADR 29). The looser-change rule is
 * [GoalDeepWorkWeeklyRules]: raising applies at once; lowering or removing waits the delay, is refused during a session (outside
 * ProductaBuddy mode) and never applies while one runs; there is no "apply now". A waiting change is settled whenever it is read, so
 * it also holds across a restart; nothing here is read by a guard, the goal unlock or `focusDay`.
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

    /** The weekly target now: a waiting change whose time has come, with no session running, applies first. */
    @Synchronized
    fun current(): GoalDeepWorkWeekly {
        val env = environment()
        val settled = GoalDeepWorkWeeklyRules.settle(_state.value, env.productaBuddy, env.sessionRunning, env.lowerWaitsNow, env.now, env.looserDelay)
        if (settled != _state.value) save(settled)
        return _state.value
    }

    /** Asks for [minutes] (null = none); what happened. */
    @Synchronized
    fun set(minutes: Int?): GoalDeepWorkWeeklyResult {
        val env = environment()
        val change = GoalDeepWorkWeeklyRules.set(current(), minutes, env.productaBuddy, env.sessionRunning, env.lowerWaitsNow, env.now, env.looserDelay)
        if (change.state != _state.value) save(change.state)
        return change.result
    }

    /** Cancel on a waiting change (the goal page, Edit goal). */
    @Synchronized
    fun cancel() {
        val cancelled = GoalDeepWorkWeeklyRules.cancel(_state.value)
        if (cancelled != _state.value) save(cancelled)
    }

    /** A backup's weekly target, through the same rules as a change made here (plan §3b 6). */
    @Synchronized
    fun restore(backupMinutes: Int?): GoalDeepWorkWeeklyResult {
        val env = environment()
        val change = GoalDeepWorkWeeklyRules.restore(current(), backupMinutes, env.productaBuddy, env.sessionRunning, env.lowerWaitsNow, env.now, env.looserDelay)
        if (change.state != _state.value) save(change.state)
        return change.result
    }

    private fun save(state: GoalDeepWorkWeekly) {
        _state.value = state
        val pending = state.pending
        prefs.edit()
            .putInt(MINUTES, state.minutes ?: NONE)
            .putBoolean(PENDING, pending != null)
            .putInt(PENDING_MINUTES, pending?.amount?.toInt() ?: NONE)
            .putString(PENDING_AT, pending?.let { GoalJson.time(it.appliesAt) })
            .putBoolean(PENDING_PB, pending?.madeInProductaBuddy ?: false)
            .apply()
        onChanged()
    }

    private fun load(): GoalDeepWorkWeekly {
        val minutes = prefs.getInt(MINUTES, NONE).takeIf { it != NONE }
        if (!prefs.getBoolean(PENDING, false)) return GoalDeepWorkWeekly(minutes)
        val at = prefs.getString(PENDING_AT, null)?.let { runCatching { GoalJson.parseTime(it) }.getOrNull() } ?: return GoalDeepWorkWeekly(minutes)
        val amount = prefs.getInt(PENDING_MINUTES, NONE).takeIf { it != NONE }?.toDouble()
        return GoalDeepWorkWeekly(minutes, GoalPendingChange(amount, at, prefs.getBoolean(PENDING_PB, false)))
    }

    companion object {
        const val FILE = "goalsDeepWork"
        private const val NONE = -1
        private const val MINUTES = "weeklyMinutes"
        private const val PENDING = "pending"
        private const val PENDING_MINUTES = "pendingMinutes"
        private const val PENDING_AT = "pendingAt"
        private const val PENDING_PB = "pendingMadeInProductaBuddy"
    }
}
