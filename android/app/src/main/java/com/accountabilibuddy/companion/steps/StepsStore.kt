package com.accountabilibuddy.companion.steps

import android.content.Context
import com.accountabilibuddy.companion.health.StepsReader
import com.accountabilibuddy.shared.sync.SyncStepDay
import com.accountabilibuddy.shared.sync.SyncSteps
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.time.Duration
import java.time.Instant
import java.time.LocalDate

/**
 * Daily steps on the phone. Preferences "stepsSettings" (the switch and the goal, in the settings backup) and "stepsData" (the last read:
 * when, and the counts; never backed up). Off by default; turning it on asks for the Health Connect steps permission
 * ([permissionRequests]), turning it off forgets the counts. [readIfDue] reads at most every [MIN_GAP]: with the app in front, and in
 * the background sync worker only when Health Connect's background-read permission was granted. The counts go to the user's own devices
 * in the phone's sealed sync file ([forSync]).
 */
class StepsStore(private val context: Context) {
    private val settings = context.getSharedPreferences(SETTINGS_PREFS, Context.MODE_PRIVATE)
    private val data = context.getSharedPreferences(DATA_PREFS, Context.MODE_PRIVATE)
    private val reader = StepsReader(context)
    private val readLock = Mutex()
    private val _state = MutableStateFlow(load())
    private val _permissionRequests = MutableSharedFlow<Unit>(extraBufferCapacity = 1)

    // Stamped before a read starts, so the pace holds even when a read is slow or fails.
    @Volatile
    private var lastAttemptAt: Instant? = null

    val state: StateFlow<StepsState> = _state.asStateFlow()

    /** Emitted when the switch is turned on, or the card is tapped to ask again: the activity asks Health Connect for the permission. */
    val permissionRequests: SharedFlow<Unit> = _permissionRequests.asSharedFlow()

    /** Called after the switch, the goal or the counts changed, so the PCs hear of it. */
    var onChanged: (() -> Unit)? = null

    fun setEnabled(on: Boolean) {
        if (on == _state.value.enabled) return
        settings.edit().putBoolean(ENABLED_KEY, on).apply()
        if (on) {
            _state.value = _state.value.copy(enabled = true, problem = StepsProblem.None)
            _permissionRequests.tryEmit(Unit)
        } else {
            data.edit().clear().apply()
            _state.value = _state.value.copy(enabled = false, readAt = null, days = emptyList(), problem = StepsProblem.None)
            onChanged?.invoke()
        }
    }

    /** The card was tapped: ask Health Connect for the permission again. */
    fun askAgain() {
        if (_state.value.enabled) _permissionRequests.tryEmit(Unit)
    }

    /** Settings → Your day → "Daily steps": the goal, 1,000 to 50,000 in steps of 500. */
    fun setGoal(goal: Int) {
        val clamped = (Math.round(goal / GOAL_STEP.toDouble()).toInt() * GOAL_STEP).coerceIn(MIN_GOAL, MAX_GOAL)
        if (clamped == _state.value.goal) return
        settings.edit().putInt(GOAL_KEY, clamped).apply()
        _state.value = _state.value.copy(goal = clamped)
        onChanged?.invoke()
    }

    /** True when Health Connect is on this phone (the switch can't work without it). */
    fun isAvailable(): Boolean = reader.isAvailable()

    /**
     * The permissions to ask for: steps, and the background-read permission when this Health Connect offers it (declined or not offered
     * leaves reading to when the app is in front).
     */
    suspend fun permissionsToRequest(): Set<String> =
        reader.permissions + if (runCatching { reader.backgroundReadOffered() }.getOrDefault(false)) reader.backgroundPermissions else emptySet()

    /**
     * Reads today's steps and the days before when the switch is on and the last attempt is older than [MIN_GAP] (or [force]), and sets
     * [StepsState.problem] when it can't. [inBackground] (the sync worker): reads only when the background-read permission is granted,
     * since Health Connect refuses a read from a background app without it. With [notify] false the caller sends the file itself.
     *
     * @return Why a read failed, for [force]; null otherwise.
     */
    suspend fun readIfDue(now: Instant = Instant.now(), force: Boolean = false, inBackground: Boolean = false, notify: Boolean = true): String? {
        if (!_state.value.enabled) return null
        return readLock.withLock {
            if (!_state.value.enabled || !(force || isDue(now))) return@withLock null
            if (!reader.isAvailable()) {
                setProblem(StepsProblem.NotAvailable)
                return@withLock null
            }

            val granted = runCatching { reader.grantedPermissions() }.getOrDefault(emptySet())
            if (!granted.containsAll(reader.permissions)) {
                setProblem(StepsProblem.NeedsPermission)
                return@withLock null
            }

            if (inBackground && !granted.containsAll(reader.backgroundPermissions)) return@withLock null
            lastAttemptAt = now
            runCatching { withContext(Dispatchers.IO) { reader.readDays(KEPT_DAYS) } }
                .onSuccess { record(it, now, notify) }
                .exceptionOrNull()?.let { if (force) "Couldn't read steps: ${it.message}" else null }
        }
    }

    private fun isDue(now: Instant): Boolean {
        val last = listOfNotNull(lastAttemptAt, _state.value.readAt).maxOrNull() ?: return true
        return Duration.between(last, now) >= MIN_GAP
    }

    private fun setProblem(problem: StepsProblem) {
        if (_state.value.problem != problem) _state.value = _state.value.copy(problem = problem)
    }

    // Keeps what Health Connect gave; tells the PCs only when something changed.
    private fun record(days: List<SyncStepDay>, readAt: Instant, notify: Boolean) {
        val current = _state.value
        if (!current.enabled) return
        val kept = days.sortedBy { it.date }.takeLast(KEPT_DAYS)
        val changed = kept != current.days
        data.edit().putLong(READ_AT_KEY, readAt.toEpochMilli()).putString(DAYS_KEY, kept.joinToString(",") { "${it.date}:${it.count}" }).apply()
        _state.value = current.copy(readAt = readAt, days = kept, problem = StepsProblem.None)
        if (changed && notify) onChanged?.invoke()
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
        if (!enabled) {
            // A restored backup with the switch off leaves no counts behind.
            if (data.all.isNotEmpty()) data.edit().clear().apply()
            return StepsState(false, goal)
        }
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

        /** The least time between two reads: 3 minutes less a little, for the timing of the 30-second tick and the worker. */
        val MIN_GAP: Duration = Duration.ofSeconds(170)
    }
}
