package com.accountabilibuddy.companion.timers

import android.content.Context
import com.accountabilibuddy.companion.engine.ClockFormat
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneId

/**
 * The phone's own timers and alarms (V1): named, editable, several at once, kept in prefs "timers". Nothing here leaves
 * the phone: not in the sync file, the settings backup, the link or the watch. A running timer keeps its end time and
 * an alarm its next ring, so both come back after a restart ([TimerScheduler]). Timers never touch focus sessions.
 */
class TimersStore(private val context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private var nextId = prefs.getInt(KEY_NEXT_ID, 1)
    private val _timers = MutableStateFlow(readTimers())
    private val _alarms = MutableStateFlow(readAlarms())

    val timers: StateFlow<List<TimerItem>> = _timers.asStateFlow()
    val alarms: StateFlow<List<AlarmItem>> = _alarms.asStateFlow()

    // ---- Timers ----

    /** A new timer ([name] blank = "Timer"), started at once when [start]. */
    @Synchronized
    fun addTimer(name: String, lengthMs: Long, start: Boolean = true, nowMs: Long = System.currentTimeMillis()) {
        val length = lengthMs.coerceIn(MIN_LENGTH_MS, MAX_LENGTH_MS)
        val timer = TimerItem(newId(), name.trim().ifEmpty { TimerItem.DEFAULT_NAME }, length)
        saveTimers(_timers.value + if (start) started(timer, nowMs) else timer)
    }

    /** Starts a set or finished timer from its whole length. */
    @Synchronized
    fun start(id: Int, nowMs: Long = System.currentTimeMillis()) {
        if (_timers.value.any { it.id == id && it.state == TimerState.DONE }) TimerRinger.stop(context, RingingItem.Kind.TIMER, id)
        updateTimer(id) { if (it.state == TimerState.IDLE || it.state == TimerState.DONE) started(it, nowMs) else it }
    }

    @Synchronized
    fun pause(id: Int, nowMs: Long = System.currentTimeMillis()) = updateTimer(id) {
        if (it.state != TimerState.RUNNING) it else it.copy(state = TimerState.PAUSED, remainingMs = it.leftMs(nowMs), endAtMs = null)
    }

    @Synchronized
    fun resume(id: Int, nowMs: Long = System.currentTimeMillis()) = updateTimer(id) {
        if (it.state != TimerState.PAUSED) it else it.copy(state = TimerState.RUNNING, endAtMs = nowMs + (it.remainingMs ?: it.lengthMs), remainingMs = null)
    }

    /** +1 min: on a running or paused timer, a minute more (the bar grows with it); on one that's up, a fresh minute. */
    @Synchronized
    fun addMinute(id: Int, nowMs: Long = System.currentTimeMillis()) {
        if (_timers.value.any { it.id == id && it.state == TimerState.DONE }) TimerRinger.stop(context, RingingItem.Kind.TIMER, id)
        updateTimer(id) {
            when (it.state) {
                TimerState.RUNNING -> it.copy(endAtMs = (it.endAtMs ?: nowMs) + MINUTE_MS, totalMs = it.totalMs + MINUTE_MS)
                TimerState.PAUSED -> it.copy(remainingMs = (it.remainingMs ?: it.lengthMs) + MINUTE_MS, totalMs = it.totalMs + MINUTE_MS)
                TimerState.DONE -> it.copy(state = TimerState.RUNNING, endAtMs = nowMs + MINUTE_MS, remainingMs = null, totalMs = MINUTE_MS)
                TimerState.IDLE -> it
            }
        }
    }

    /** Back to its whole length, not running (also Dismiss on one that's up or running). */
    @Synchronized
    fun reset(id: Int) {
        TimerRinger.stop(context, RingingItem.Kind.TIMER, id)
        updateTimer(id) { it.copy(state = TimerState.IDLE, endAtMs = null, remainingMs = null, totalMs = it.lengthMs) }
    }

    /**
     * A new name and length. A running timer starts over with the new length from now; a paused one gets it as its
     * time left; a set one simply takes it.
     */
    @Synchronized
    fun editTimer(id: Int, name: String, lengthMs: Long, nowMs: Long = System.currentTimeMillis()) {
        val length = lengthMs.coerceIn(MIN_LENGTH_MS, MAX_LENGTH_MS)
        // One that's up stops ringing and is set again with the new length.
        if (_timers.value.any { it.id == id && it.state == TimerState.DONE }) TimerRinger.stop(context, RingingItem.Kind.TIMER, id)
        updateTimer(id) {
            val renamed = it.copy(name = name.trim().ifEmpty { TimerItem.DEFAULT_NAME }, lengthMs = length)
            when (it.state) {
                TimerState.RUNNING -> started(renamed, nowMs)
                TimerState.PAUSED -> renamed.copy(remainingMs = length, totalMs = length)
                TimerState.IDLE -> renamed.copy(totalMs = length)
                TimerState.DONE -> renamed.copy(state = TimerState.IDLE, endAtMs = null, remainingMs = null, totalMs = length)
            }
        }
    }

    @Synchronized
    fun deleteTimer(id: Int) {
        TimerRinger.stop(context, RingingItem.Kind.TIMER, id)
        TimerNotifications.cancel(context, RingingItem.Kind.TIMER, id)
        saveTimers(_timers.value.filterNot { it.id == id })
    }

    // ---- Alarms ----

    @Synchronized
    fun addAlarm(name: String, time: LocalTime, days: Set<DayOfWeek>, nowMs: Long = System.currentTimeMillis()) {
        val alarm = AlarmItem(newId(), name.trim().ifEmpty { AlarmItem.DEFAULT_NAME }, time.withSecond(0).withNano(0), days)
        saveAlarms(_alarms.value + armed(alarm, nowMs))
    }

    /** A new name, time and days; the alarm is turned on with its next ring worked out again. */
    @Synchronized
    fun editAlarm(id: Int, name: String, time: LocalTime, days: Set<DayOfWeek>, nowMs: Long = System.currentTimeMillis()) = updateAlarm(id) {
        armed(it.copy(name = name.trim().ifEmpty { AlarmItem.DEFAULT_NAME }, time = time.withSecond(0).withNano(0), days = days, snoozeUntilMs = null), nowMs)
    }

    @Synchronized
    fun setAlarmOn(id: Int, on: Boolean, nowMs: Long = System.currentTimeMillis()) {
        if (!on) TimerRinger.stop(context, RingingItem.Kind.ALARM, id)
        updateAlarm(id) { if (on) armed(it, nowMs) else it.copy(on = false, nextMs = null, snoozeUntilMs = null) }
    }

    /** Stops a ringing alarm (it rings next on its next day; a once alarm is already off). */
    @Synchronized
    fun dismissAlarm(id: Int) {
        TimerRinger.stop(context, RingingItem.Kind.ALARM, id)
        updateAlarm(id) { it.copy(snoozeUntilMs = null) }
    }

    /** Rings again in 9 minutes. */
    @Synchronized
    fun snoozeAlarm(id: Int, nowMs: Long = System.currentTimeMillis()) {
        TimerRinger.stop(context, RingingItem.Kind.ALARM, id)
        updateAlarm(id) { it.copy(snoozeUntilMs = nowMs + SNOOZE_MS) }
    }

    @Synchronized
    fun deleteAlarm(id: Int) {
        TimerRinger.stop(context, RingingItem.Kind.ALARM, id)
        TimerNotifications.cancel(context, RingingItem.Kind.ALARM, id)
        saveAlarms(_alarms.value.filterNot { it.id == id })
    }

    /** The clock or time zone changed: each alarm that's on rings next at its time on the new clock. */
    @Synchronized
    fun rearmAlarms(nowMs: Long = System.currentTimeMillis()) =
        saveAlarms(_alarms.value.map { if (it.on) armed(it, nowMs) else it })

    // ---- Due ----

    /** The soonest end of a running timer or ring of an alarm (or its snooze); null when nothing's waiting. */
    fun nextDueMs(): Long? =
        (_timers.value.filter { it.state == TimerState.RUNNING }.mapNotNull { it.endAtMs } + _alarms.value.mapNotNull { it.dueMs() }).minOrNull()

    fun anyRunning(): Boolean = _timers.value.any { it.state == TimerState.RUNNING }

    /**
     * Marks every timer that ended and every alarm that's due by [nowMs] (a few seconds early counts) and returns what
     * should ring. One missed by more than an hour (the phone was off) doesn't ring: a timer is just up, a once alarm
     * turns off, a repeating one moves to its next day.
     */
    @Synchronized
    fun takeDue(nowMs: Long = System.currentTimeMillis()): List<RingingItem> {
        val ring = mutableListOf<RingingItem>()
        val dueBy = nowMs + EARLY_MS
        val timers = _timers.value.map { timer ->
            val end = timer.endAtMs
            if (timer.state != TimerState.RUNNING || end == null || end > dueBy) return@map timer
            if (nowMs - end <= MISSED_MS) {
                ring += RingingItem(RingingItem.Kind.TIMER, timer.id, "Timer · ${timer.name} is up", "${TimerItem.formatLength(timer.totalMs)} timer")
            }
            timer.copy(state = TimerState.DONE, endAtMs = null, remainingMs = null)
        }
        val clock = ClockFormat(is24Hour = false)
        val alarms = _alarms.value.map { alarm ->
            val snoozeDue = alarm.snoozeUntilMs?.let { it <= dueBy } == true
            val next = alarm.nextMs
            val nextDue = alarm.on && next != null && next <= dueBy
            if (!snoozeDue && !nextDue) return@map alarm
            val at = listOfNotNull(alarm.snoozeUntilMs.takeIf { snoozeDue }, next.takeIf { nextDue }).max()
            if (nowMs - at <= MISSED_MS) {
                val time = clock.time(LocalDateTime.ofInstant(Instant.ofEpochMilli(at), ZoneId.systemDefault()))
                ring += RingingItem(RingingItem.Kind.ALARM, alarm.id, alarm.name, "Alarm · $time")
            }
            var updated = if (snoozeDue) alarm.copy(snoozeUntilMs = null) else alarm
            if (nextDue) {
                updated = if (alarm.once) updated.copy(on = false, nextMs = null) else updated.copy(nextMs = alarm.nextAfterMs(maxOf(nowMs, next)))
            }
            updated
        }
        saveTimers(timers, reschedule = false)
        saveAlarms(alarms, reschedule = false)
        return ring
    }

    // ---- Helpers ----

    private fun started(timer: TimerItem, nowMs: Long) =
        timer.copy(state = TimerState.RUNNING, endAtMs = nowMs + timer.lengthMs, remainingMs = null, totalMs = timer.lengthMs)

    private fun armed(alarm: AlarmItem, nowMs: Long) = alarm.copy(on = true, nextMs = alarm.nextAfterMs(nowMs))

    private fun newId(): Int {
        val id = nextId++
        prefs.edit().putInt(KEY_NEXT_ID, nextId).apply()
        return id
    }

    private fun updateTimer(id: Int, change: (TimerItem) -> TimerItem) =
        saveTimers(_timers.value.map { if (it.id == id) change(it) else it })

    private fun updateAlarm(id: Int, change: (AlarmItem) -> AlarmItem) =
        saveAlarms(_alarms.value.map { if (it.id == id) change(it) else it })

    private fun saveTimers(timers: List<TimerItem>, reschedule: Boolean = true) {
        _timers.value = timers
        val array = JSONArray()
        timers.forEach { timer ->
            array.put(
                JSONObject()
                    .put("id", timer.id)
                    .put("name", timer.name)
                    .put("lengthMs", timer.lengthMs)
                    .put("state", timer.state.name)
                    .put("totalMs", timer.totalMs)
                    .apply {
                        timer.endAtMs?.let { put("endAtMs", it) }
                        timer.remainingMs?.let { put("remainingMs", it) }
                    },
            )
        }
        prefs.edit().putString(KEY_TIMERS, array.toString()).apply()
        if (reschedule) TimerScheduler.apply(context, this)
    }

    private fun saveAlarms(alarms: List<AlarmItem>, reschedule: Boolean = true) {
        _alarms.value = alarms
        val array = JSONArray()
        alarms.forEach { alarm ->
            array.put(
                JSONObject()
                    .put("id", alarm.id)
                    .put("name", alarm.name)
                    .put("time", alarm.time.toString())
                    .put("days", JSONArray(alarm.days.sorted().map { it.name }))
                    .put("on", alarm.on)
                    .apply {
                        alarm.nextMs?.let { put("nextMs", it) }
                        alarm.snoozeUntilMs?.let { put("snoozeUntilMs", it) }
                    },
            )
        }
        prefs.edit().putString(KEY_ALARMS, array.toString()).apply()
        if (reschedule) TimerScheduler.apply(context, this)
    }

    private fun readTimers(): List<TimerItem> = readArray(KEY_TIMERS) { json ->
        val length = json.getLong("lengthMs")
        TimerItem(
            id = json.getInt("id"),
            name = json.optString("name", TimerItem.DEFAULT_NAME),
            lengthMs = length,
            state = runCatching { TimerState.valueOf(json.getString("state")) }.getOrDefault(TimerState.IDLE),
            endAtMs = json.optLongOrNull("endAtMs"),
            remainingMs = json.optLongOrNull("remainingMs"),
            totalMs = json.optLong("totalMs", length),
        )
    }

    private fun readAlarms(): List<AlarmItem> = readArray(KEY_ALARMS) { json ->
        val days = json.optJSONArray("days")
        AlarmItem(
            id = json.getInt("id"),
            name = json.optString("name", AlarmItem.DEFAULT_NAME),
            time = LocalTime.parse(json.getString("time")),
            days = (0 until (days?.length() ?: 0)).mapNotNull { runCatching { DayOfWeek.valueOf(days!!.getString(it)) }.getOrNull() }.toSet(),
            on = json.optBoolean("on", false),
            nextMs = json.optLongOrNull("nextMs"),
            snoozeUntilMs = json.optLongOrNull("snoozeUntilMs"),
        )
    }

    // A broken entry is skipped, not the whole list.
    private fun <T> readArray(key: String, parse: (JSONObject) -> T): List<T> {
        val text = prefs.getString(key, null) ?: return emptyList()
        val array = runCatching { JSONArray(text) }.getOrNull() ?: return emptyList()
        return (0 until array.length()).mapNotNull { index -> runCatching { parse(array.getJSONObject(index)) }.getOrNull() }
    }

    private fun JSONObject.optLongOrNull(name: String): Long? = if (has(name) && !isNull(name)) getLong(name) else null

    companion object {
        private const val PREFS = "timers"
        private const val KEY_TIMERS = "timers"
        private const val KEY_ALARMS = "alarms"
        private const val KEY_NEXT_ID = "nextId"
        private const val MINUTE_MS = 60_000L
        private const val SNOOZE_MS = 9 * MINUTE_MS
        private const val EARLY_MS = 3_000L
        private const val MISSED_MS = 60 * MINUTE_MS
        const val MIN_LENGTH_MS = 5_000L
        const val MAX_LENGTH_MS = 24 * 60 * MINUTE_MS

        /** The quick timers on Today, in minutes. */
        val PRESETS = listOf(5, 10, 15, 25)
    }
}
