package com.accountabilibuddy.companion.lunch

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.shared.WatchLunch
import com.accountabilibuddy.shared.sync.SyncLunch
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * The flexible lunch on the phone (schema v8, as the PC's LunchService): on any workday "Start lunch" any time, for
 * the lunch length (this phone's setting, 1 hour by default; it wins over a flexible break's own length); while it
 * runs, +5 / −5 min change its end. The same lunch on every device: its sync file carries it, and the newest edit for
 * the day wins (`editedAt`, else its start). "Lunch over!" comes from an alarm at its end. Preferences "lunch" (today's
 * state) and "lunchSettings" (the length, in the settings backup).
 */
class LunchStore(private val context: Context) {
    private val prefs = context.getSharedPreferences("lunch", Context.MODE_PRIVATE)
    private val settingsPrefs = context.getSharedPreferences(SETTINGS_PREFS, Context.MODE_PRIVATE)
    private val _lunch = MutableStateFlow(load())
    private val _lengthMinutes = MutableStateFlow(clampLength(settingsPrefs.getInt(LENGTH_KEY, DEFAULT_LENGTH_MINUTES)))

    /** Today's lunch once started (it may be over); null before. */
    val lunch: StateFlow<SyncLunch?> = _lunch.asStateFlow()

    /** How long a lunch lasts when it starts, in minutes (Settings → Your day). */
    val lengthMinutes: StateFlow<Int> = _lengthMinutes.asStateFlow()

    /** Called after lunch starts, changes or ends here (the PC hears of it; the watch follows [lunch]). */
    var onChanged: (() -> Unit)? = null

    fun today(now: Instant = Instant.now()): SyncLunch? = _lunch.value?.takeIf { isToday(it.start, now) }

    fun isOnLunch(now: Instant = Instant.now()): Boolean = today(now)?.let { now < it.end } == true

    /** "Start lunch" shows: today is a workday (or has a flexible break) and no lunch was taken yet. */
    fun canStart(plan: DayPlan, now: Instant = Instant.now()): Boolean {
        val date = now.atZone(ZoneId.systemDefault()).toLocalDate()
        return (plan.isWorkday(date) || plan.flexibleBreakOn(date) != null) && today(now) == null
    }

    /** −5 min can still take something off: the running lunch ends more than a minute from now. */
    fun canShorten(now: Instant = Instant.now()): Boolean =
        today(now)?.let { now < it.end && it.end > now.plus(WatchLunch.SHORTEST_LEFT) } == true

    /** Starts lunch now for the lunch length. */
    @Synchronized
    fun start(now: Instant = Instant.now()) {
        save(clamped(SyncLunch(now, now.plus(Duration.ofMinutes(_lengthMinutes.value.toLong())), editedAt = now)), overTold = false)
        onChanged?.invoke()
    }

    /**
     * +5 min ([longer]) or −5 min on the running lunch; it never ends sooner than a minute from now, −5 never makes it
     * longer, and +5 never takes the whole lunch past [MAX_LENGTH].
     */
    @Synchronized
    fun adjust(longer: Boolean, now: Instant = Instant.now()) {
        val current = today(now)?.takeIf { now < it.end } ?: return
        var end = if (longer) current.end.plus(ADJUST_STEP) else current.end.minus(ADJUST_STEP)
        val soonest = now.plus(WatchLunch.SHORTEST_LEFT)
        if (end < soonest) {
            end = if (soonest < current.end) soonest else current.end
        }

        val latestEnd = current.start.plus(MAX_LENGTH)
        if (end > latestEnd) {
            end = if (latestEnd > current.end) latestEnd else current.end
        }

        if (end != current.end) {
            save(current.copy(end = end, editedAt = now), overTold = false)
            onChanged?.invoke()
        }
    }

    /** Back early: lunch ends now. */
    @Synchronized
    fun endNow(now: Instant = Instant.now()) {
        val current = today(now)?.takeIf { now < it.end } ?: return
        save(current.copy(end = now, editedAt = now), overTold = true)
        onChanged?.invoke()
    }

    /** Settings → Your day → "Lunch length": 15-120 minutes in steps of 5; the next lunch takes it. */
    fun setLength(minutes: Int) {
        val clamped = clampLength(Math.round(minutes / LENGTH_STEP.toDouble()).toInt() * LENGTH_STEP)
        if (clamped == _lengthMinutes.value) return
        settingsPrefs.edit().putInt(LENGTH_KEY, clamped).apply()
        // This phone's own setting: nothing to sync; the watch hears of it through [lengthMinutes].
        _lengthMinutes.value = clamped
    }

    /** A lunch from another device's file: taken when it's today's and its edit is newer than ours. */
    @Synchronized
    fun receive(received: SyncLunch, now: Instant = Instant.now()) {
        if (!isToday(received.start, now)) return
        val lunch = clamped(received)
        val ours = today(now)
        if (ours != null && (ours.lastEdit >= lunch.lastEdit || (ours.start == lunch.start && ours.end == lunch.end))) return
        save(lunch, overTold = now >= lunch.end)
    }

    /** Today's lunch for the watch's Lunch page: shown on a workday (or a day with a flexible break), or while lunch runs. */
    fun forWatch(plan: DayPlan, now: Instant = Instant.now()): WatchLunch {
        val date = now.atZone(ZoneId.systemDefault()).toLocalDate()
        val taken = today(now)
        return WatchLunch(
            workday = plan.isWorkday(date) || plan.flexibleBreakOn(date) != null,
            lengthMinutes = _lengthMinutes.value,
            start = taken?.start,
            end = taken?.end,
        )
    }

    // A whole lunch (start to end) is never longer than MAX_LENGTH, as on the PC: lunch holds a session's step away there.
    private fun clamped(lunch: SyncLunch): SyncLunch {
        val latestEnd = lunch.start.plus(MAX_LENGTH)
        return if (lunch.end.isAfter(latestEnd)) lunch.copy(end = latestEnd) else lunch
    }

    /** "Lunch over!" is due and not told yet. */
    fun overDue(now: Instant = Instant.now()): Boolean = today(now)?.let { now >= it.end } == true && !prefs.getBoolean("overTold", false)

    fun markOverTold() {
        prefs.edit().putBoolean("overTold", true).apply()
    }

    /** The hint went out on [day]. */
    fun hintedOn(day: LocalDate): Boolean = prefs.getString("hintedOn", null) == day.toString()

    fun markHinted(day: LocalDate) {
        prefs.edit().putString("hintedOn", day.toString()).apply()
    }

    private fun save(lunch: SyncLunch, overTold: Boolean) {
        _lunch.value = lunch
        val editor = prefs.edit()
            .putString("start", lunch.start.toString())
            .putString("end", lunch.end.toString())
            .putBoolean("overTold", overTold)
        if (lunch.editedAt != null) {
            editor.putString("editedAt", lunch.editedAt.toString())
        } else {
            editor.remove("editedAt")
        }
        editor.apply()
        scheduleOver(lunch, overTold)
    }

    // The "Lunch over!" alarm at its end (inexact but allowed while idle: no special permission).
    private fun scheduleOver(lunch: SyncLunch, overTold: Boolean) {
        val alarms = context.getSystemService(AlarmManager::class.java) ?: return
        val intent = PendingIntent.getBroadcast(
            context, 52, Intent(context, LunchReceiver::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        alarms.cancel(intent)
        if (!overTold && lunch.end.isAfter(Instant.now())) {
            alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, lunch.end.toEpochMilli(), intent)
        }
    }

    private fun load(): SyncLunch? {
        val start = prefs.getString("start", null)?.let { runCatching { Instant.parse(it) }.getOrNull() } ?: return null
        val end = prefs.getString("end", null)?.let { runCatching { Instant.parse(it) }.getOrNull() } ?: return null
        val editedAt = prefs.getString("editedAt", null)?.let { runCatching { Instant.parse(it) }.getOrNull() }
        return SyncLunch(start, end, editedAt)
    }

    private fun isToday(time: Instant, now: Instant): Boolean {
        val zone = ZoneId.systemDefault()
        return time.atZone(zone).toLocalDate() == now.atZone(zone).toLocalDate()
    }

    companion object {
        /** The preferences file with the lunch length (backed up with the phone's settings). */
        const val SETTINGS_PREFS = "lunchSettings"

        /** The lunch length setting's range and step, in minutes (as on the PC). */
        const val MIN_LENGTH_MINUTES = 15
        const val MAX_LENGTH_MINUTES = 120
        const val LENGTH_STEP = 5
        const val DEFAULT_LENGTH_MINUTES = 60

        private const val LENGTH_KEY = "lengthMinutes"

        /** How much +5 / −5 min changes a running lunch by. */
        private val ADJUST_STEP: Duration = Duration.ofMinutes(5)

        private fun clampLength(minutes: Int): Int = minutes.coerceIn(MIN_LENGTH_MINUTES, MAX_LENGTH_MINUTES)

        /** A lunch length as "1 h", "1 h 5 m" or "45 m" (as on the PC). */
        fun formatLength(minutes: Int): String {
            val hours = minutes / 60
            val rest = minutes % 60
            return when {
                hours == 0 -> "$rest m"
                rest == 0 -> "$hours h"
                else -> "$hours h $rest m"
            }
        }

        /** The longest a whole lunch can be (the PC's LunchService.MaxLength). */
        val MAX_LENGTH: Duration = Duration.ofMinutes(120)
    }
}
