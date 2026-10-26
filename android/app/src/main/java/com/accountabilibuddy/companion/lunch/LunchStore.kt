package com.accountabilibuddy.companion.lunch

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.shared.sync.SyncLunch
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * The flexible lunch on the phone (schema v8, as the PC's LunchService): "Start lunch" any time, for the plan's length
 * (1 hour by default); the same lunch on every device (its sync file carries it; the newest for the day wins).
 * "Lunch over!" comes from an alarm at its end. Preferences "lunch".
 */
class LunchStore(private val context: Context) {
    private val prefs = context.getSharedPreferences("lunch", Context.MODE_PRIVATE)
    private val _lunch = MutableStateFlow(load())

    /** Today's lunch once started (it may be over); null before. */
    val lunch: StateFlow<SyncLunch?> = _lunch.asStateFlow()

    /** Called after lunch starts or ends here (the PC hears of it). */
    var onChanged: (() -> Unit)? = null

    fun today(now: Instant = Instant.now()): SyncLunch? = _lunch.value?.takeIf { isToday(it.start, now) }

    fun isOnLunch(now: Instant = Instant.now()): Boolean = today(now)?.let { now < it.end } == true

    @Synchronized
    fun start(minutes: Int, now: Instant = Instant.now()) {
        save(clamped(SyncLunch(now, now.plus(Duration.ofMinutes(minutes.toLong())))), overTold = false)
        onChanged?.invoke()
    }

    /** Back early: lunch ends now. */
    @Synchronized
    fun endNow(now: Instant = Instant.now()) {
        val current = today(now)?.takeIf { now < it.end } ?: return
        save(current.copy(end = now), overTold = true)
        onChanged?.invoke()
    }

    /** A lunch from another device's file: taken when it's today's and at least as new as ours. */
    @Synchronized
    fun receive(received: SyncLunch, now: Instant = Instant.now()) {
        if (!isToday(received.start, now)) return
        val lunch = clamped(received)
        val ours = today(now)
        if (ours != null && (ours.start > lunch.start || ours == lunch)) return
        save(lunch, overTold = now >= lunch.end)
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
        prefs.edit().putString("start", lunch.start.toString()).putString("end", lunch.end.toString()).putBoolean("overTold", overTold).apply()
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
        return SyncLunch(start, end)
    }

    private fun isToday(time: Instant, now: Instant): Boolean {
        val zone = ZoneId.systemDefault()
        return time.atZone(zone).toLocalDate() == now.atZone(zone).toLocalDate()
    }

    companion object {
        /** The longest a whole lunch can be (the PC's LunchService.MaxLength). */
        val MAX_LENGTH: Duration = Duration.ofMinutes(120)
    }
}
