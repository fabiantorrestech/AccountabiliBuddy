package com.accountabilibuddy.companion.goals

import android.content.Context
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.shared.goals.GoalDeepWorkDaily
import com.accountabilibuddy.shared.goals.GoalDeepWorkDailyChange
import com.accountabilibuddy.shared.goals.GoalDeepWorkDailyRules
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalPendingChange
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import java.time.OffsetDateTime
import java.time.ZoneId
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Deep work's daily target on a phone with no PC in its group (NG1; the pure rule is [GoalDeepWorkDailyRules]): this phone's own, in the
 * plain preferences "goalsDeepWorkDaily" (minutes, a waiting change; no text, no stamp, never sent). It settles like the weekly target
 * ([PhoneDeepWorkWeekly]) and reads the same environment (mode, session, delay) from [environment]; until the app says what those are,
 * nothing waiting is applied. While a PC is in the group this value is not used ([GoalDeepWorkDailyRules.effective]).
 */
class PhoneDeepWorkDaily(context: Context) {
    private val prefs = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
    private val _state = MutableStateFlow(load())

    /** The stored state as last read or written (the screens read [current], which settles first). */
    val state: StateFlow<GoalDeepWorkDaily> = _state.asStateFlow()

    /** Set by the store: the same environment as the weekly target's. */
    var environment: () -> PhoneWeeklyEnvironment = {
        PhoneWeeklyEnvironment.unknown(OffsetDateTime.ofInstant(TrustedTime.now(), ZoneId.systemDefault()))
    }

    /** Set by the app: the daily target or its waiting change was written (an auto-backup soon). */
    var onChanged: () -> Unit = {}

    /** The daily target now: a waiting change whose time has come, with no session running, applies first. */
    @Synchronized
    fun current(): GoalDeepWorkDaily {
        val env = environment()
        if (!env.known) return _state.value
        val settled = GoalDeepWorkDailyRules.settle(_state.value, env.productaBuddy, env.sessionRunning, env.lowerWaitsNow, env.now, env.looserDelay)
        if (settled != _state.value) save(settled)
        return _state.value
    }

    /** Asks for [minutes] (null = none); what happened. */
    @Synchronized
    fun set(minutes: Int?): GoalDeepWorkWeeklyResult {
        val env = environment()
        val change: GoalDeepWorkDailyChange =
            GoalDeepWorkDailyRules.set(current(), minutes, env.productaBuddy, env.sessionRunning, env.lowerWaitsNow, env.now, env.looserDelay)
        if (change.state != _state.value) save(change.state)
        return change.result
    }

    /** Cancel on a waiting change. */
    @Synchronized
    fun cancel() {
        val cancelled = GoalDeepWorkDailyRules.cancel(_state.value)
        if (cancelled != _state.value) save(cancelled)
    }

    private fun save(state: GoalDeepWorkDaily) {
        _state.value = state
        prefs.edit()
            .putInt(MINUTES, state.minutes ?: NONE)
            .putBoolean(PENDING, state.pending != null)
            .putInt(PENDING_MINUTES, state.pending?.amount?.toInt() ?: NONE)
            .putString(PENDING_AT, state.pending?.let { GoalJson.time(it.appliesAt) })
            .putBoolean(PENDING_PB, state.pending?.madeInProductaBuddy ?: false)
            .apply()
        onChanged()
    }

    private fun load(): GoalDeepWorkDaily {
        val minutes = prefs.getInt(MINUTES, NONE).takeIf { it != NONE }
        if (!prefs.getBoolean(PENDING, false)) return GoalDeepWorkDaily(minutes)
        val at = prefs.getString(PENDING_AT, null)?.let { runCatching { GoalJson.parseTime(it) }.getOrNull() } ?: return GoalDeepWorkDaily(minutes)
        val amount = prefs.getInt(PENDING_MINUTES, NONE).takeIf { it != NONE }?.toDouble()
        return GoalDeepWorkDaily(minutes, GoalPendingChange(amount, at, prefs.getBoolean(PENDING_PB, false)))
    }

    companion object {
        const val FILE = "goalsDeepWorkDaily"
        private const val NONE = -1
        private const val MINUTES = "dailyMinutes"
        private const val PENDING = "pending"
        private const val PENDING_MINUTES = "pendingMinutes"
        private const val PENDING_AT = "pendingAt"
        private const val PENDING_PB = "pendingMadeInProductaBuddy"
    }
}
