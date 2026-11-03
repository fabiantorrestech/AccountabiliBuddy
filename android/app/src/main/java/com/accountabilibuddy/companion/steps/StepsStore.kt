package com.accountabilibuddy.companion.steps

import android.content.Context
import com.accountabilibuddy.shared.sync.SyncStepDay
import com.accountabilibuddy.shared.sync.SyncSteps
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Duration
import java.time.Instant
import java.time.LocalDate

/**
 * Daily steps on the phone. Preferences "stepsSettings" (the switch and the goal, in the settings backup) and "stepsData" (the last read:
 * when, and the counts; never backed up). Off by default; turning it on asks for the Health Connect steps permission
 * ([permissionRequests]), turning it off forgets the counts. Reads happen only while the app is in front (Health Connect's rule) and
 * at most every [MIN_GAP] (see [shouldRead]). The counts go to the user's own devices in the phone's sealed sync file ([forSync]).
 */
class StepsStore(context: Context) {
    private val settings = context.getSharedPreferences(SETTINGS_PREFS, Context.MODE_PRIVATE)
    private val data = context.getSharedPreferences(DATA_PREFS, Context.MODE_PRIVATE)
    private val _state = MutableStateFlow(load())
    private val _permissionRequests = MutableSharedFlow<Unit>(extraBufferCapacity = 1)

    val state: StateFlow<StepsState> = _state.asStateFlow()

    /** Emitted when the switch is turned on: the activity asks Health Connect for the steps permission. */
    val permissionRequests: SharedFlow<Unit> = _permissionRequests.asSharedFlow()

    /** Called after the switch, the goal or the counts changed, so the PCs hear of it. */
    var onChanged: (() -> Unit)? = null

    fun setEnabled(on: Boolean) {
        if (on == _state.value.enabled) return
        settings.edit().putBoolean(ENABLED_KEY, on).apply()
        if (on) {
            _state.value = _state.value.copy(enabled = true)
            _permissionRequests.tryEmit(Unit)
        } else {
            data.edit().clear().apply()
            _state.value = _state.value.copy(enabled = false, readAt = null, days = emptyList())
            onChanged?.invoke()
        }
    }

    /** Settings → Your day → "Daily steps": the goal, 1,000 to 50,000 in steps of 500. */
    fun setGoal(goal: Int) {
        val clamped = (Math.round(goal / GOAL_STEP.toDouble()).toInt() * GOAL_STEP).coerceIn(MIN_GOAL, MAX_GOAL)
        if (clamped == _state.value.goal) return
        settings.edit().putInt(GOAL_KEY, clamped).apply()
        _state.value = _state.value.copy(goal = clamped)
        onChanged?.invoke()
    }

    /** A read is due: the switch is on and the last read is older than [MIN_GAP] (or [force]). */
    fun shouldRead(now: Instant = Instant.now(), force: Boolean = false): Boolean {
        val current = _state.value
        if (!current.enabled) return false
        val last = current.readAt ?: return true
        return force || Duration.between(last, now) >= MIN_GAP
    }

    /** The user didn't allow the permission: the switch goes back off. */
    fun permissionDenied() = setEnabled(false)

    /** Keeps what Health Connect gave; tells the PCs only when something changed. */
    @Synchronized
    fun record(days: List<SyncStepDay>, readAt: Instant) {
        val current = _state.value
        if (!current.enabled) return
        val kept = days.sortedBy { it.date }.takeLast(KEPT_DAYS)
        val changed = kept != current.days
        data.edit().putLong(READ_AT_KEY, readAt.toEpochMilli()).putString(DAYS_KEY, kept.joinToString(",") { "${it.date}:${it.count}" }).apply()
        _state.value = current.copy(readAt = readAt, days = kept)
        if (changed) onChanged?.invoke()
    }

    /** The phone's share for its sync file: null while the switch is off or before the first read. */
    fun forSync(): SyncSteps? {
        val current = _state.value
        val readAt = current.readAt ?: return null
        if (!current.enabled) return null
        return SyncSteps(current.goal, readAt, current.days.takeLast(SyncSteps.MAX_DAYS))
    }

    private fun load(): StepsState {
        val enabled = settings.getBoolean(ENABLED_KEY, false)
        val goal = settings.getInt(GOAL_KEY, SyncSteps.DEFAULT_GOAL).coerceIn(MIN_GOAL, MAX_GOAL)
        if (!enabled) return StepsState(false, goal)
        val readAt = data.getLong(READ_AT_KEY, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli)
        val days = runCatching { parseDays(data.getString(DAYS_KEY, "").orEmpty()) }.getOrDefault(emptyList())
        return StepsState(true, goal, readAt, days)
    }

    // "2026-11-03:6240,2026-11-02:9100": a part that can't be read is skipped.
    private fun parseDays(text: String): List<SyncStepDay> = text.split(',').mapNotNull { part ->
        val pieces = part.split(':')
        if (pieces.size != 2) return@mapNotNull null
        runCatching { SyncStepDay(LocalDate.parse(pieces[0]), pieces[1].toInt()) }.getOrNull()?.takeIf { it.count >= 0 }
    }.sortedBy { it.date }

    companion object {
        const val SETTINGS_PREFS = "stepsSettings"
        const val DATA_PREFS = "stepsData"
        private const val ENABLED_KEY = "enabled"
        private const val GOAL_KEY = "goal"
        private const val READ_AT_KEY = "readAt"
        private const val DAYS_KEY = "days"

        /** Days kept on the phone for its Stats (Health Connect allows 30 days back from the first grant); the sync file carries 14. */
        const val KEPT_DAYS = 30
        const val MIN_GOAL = 1_000
        const val MAX_GOAL = 50_000
        const val GOAL_STEP = 500

        /** The least time between two reads. */
        val MIN_GAP: Duration = Duration.ofMinutes(3)
    }
}
