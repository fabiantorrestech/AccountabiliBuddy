package com.accountabilibuddy.companion.lunch

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.shared.WatchLunch
import com.accountabilibuddy.shared.sync.LunchDay
import com.accountabilibuddy.shared.sync.LunchDiscardOutcome
import com.accountabilibuddy.shared.sync.LunchDiscardSetting
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncLunch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
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
 * state) and "lunchSettings" (the length, in the settings backup). Every time comes in as `now` from the caller, which passes the phone's
 * trusted time ([com.accountabilibuddy.companion.guidance.TrustedTime], ADR 70 and 88): there is no default, so no caller can forget it.
 */
class LunchStore(private val context: Context) {
    private val prefs = context.getSharedPreferences("lunch", Context.MODE_PRIVATE)
    private val settingsPrefs = context.getSharedPreferences(SETTINGS_PREFS, Context.MODE_PRIVATE)
    private val _lunch = MutableStateFlow(load())
    private val receiving = com.accountabilibuddy.shared.sync.LunchReceiving()
    private val _lengthMinutes = MutableStateFlow(clampLength(settingsPrefs.getInt(LENGTH_KEY, DEFAULT_LENGTH_MINUTES)))

    /** Today's lunch once started (it may be over); null before. */
    val lunch: StateFlow<SyncLunch?> = _lunch.asStateFlow()

    /** How long a lunch lasts when it starts, in minutes (Settings → Your day). */
    val lengthMinutes: StateFlow<Int> = _lengthMinutes.asStateFlow()

    /** Called after lunch starts, changes or ends here (the PC hears of it; the watch follows [lunch]). */
    var onChanged: (() -> Unit)? = null

    fun today(now: Instant): SyncLunch? = _lunch.value?.takeIf { isToday(it.start, now) }

    fun isOnLunch(now: Instant): Boolean = today(now)?.let { now < it.end } == true

    /** "Start lunch" shows: today is a workday (or has a flexible break) and no lunch was taken yet. */
    fun canStart(plan: DayPlan, now: Instant): Boolean {
        val date = now.atZone(ZoneId.systemDefault()).toLocalDate()
        return (plan.isWorkday(date) || plan.flexibleBreakOn(date) != null) && today(now) == null
    }

    /** −5 min can still take something off: the running lunch ends more than a minute from now. */
    fun canShorten(now: Instant): Boolean =
        today(now)?.let { now < it.end && it.end > now.plus(WatchLunch.SHORTEST_LEFT) } == true

    /** Starts lunch now for the lunch length. */
    @Synchronized
    fun start(now: Instant) {
        // The day's first piece: its length is the day's allowance, the same on every device (ADR 88).
        save(LunchDay.begin(now, Duration.ofMinutes(_lengthMinutes.value.toLong())), overTold = false, now = now)
        onChanged?.invoke()
    }

    /**
     * +5 min ([longer]) or −5 min on the running lunch; it never ends sooner than a minute from now, −5 never makes it
     * longer, and +5 never takes the whole lunch past [MAX_LENGTH].
     */
    @Synchronized
    fun adjust(longer: Boolean, now: Instant) {
        val current = today(now)?.takeIf { now < it.end } ?: return
        val change = if (longer) ADJUST_STEP else ADJUST_STEP.negated()
        // The day's total never goes past MAX_LENGTH, never sooner than a minute from now, -5 never makes it longer; the allowance moves with it.
        val adjusted = LunchDay.adjust(current, now, change, _lengthMinutes.value) ?: return
        save(adjusted, overTold = false, now = now)
        onChanged?.invoke()
    }

    /** Back early: lunch ends now. */
    @Synchronized
    fun endNow(now: Instant) {
        val current = today(now)?.takeIf { now < it.end } ?: return
        save(current.copy(end = now, editedAt = now), overTold = true, now = now)
        onChanged?.invoke()
    }

    /**
     * "Allow discarding a lunch" (ADR 88) as this phone keeps it for a mode ([companion]: ProductaBuddy's copy, else AccountabiliBuddy's): on
     * unless turned off. Device-local and per mode (ADR 69); in the settings backup with the rest of "lunchSettings".
     */
    fun discardSetting(companion: Boolean): LunchDiscardSetting = LunchDiscardSetting(
        settingsPrefs.getBoolean(discardKey(companion, "allowed"), true),
        if (settingsPrefs.contains(discardKey(companion, "pending"))) settingsPrefs.getBoolean(discardKey(companion, "pending"), true) else null,
        settingsPrefs.getString(discardKey(companion, "pendingFrom"), null)?.let { runCatching { Instant.parse(it) }.getOrNull() },
    )

    /** Whether Discard it is offered in a mode right now (a waiting "on" does not count until it takes effect). */
    fun discardAllowed(companion: Boolean): Boolean = discardSetting(companion).allowed

    /**
     * Asks for the setting in [companion]'s copy: off applies at once, on waits [wait] and is refused while [sessionRunning]
     * ([LunchDiscardSetting.change]).
     */
    @Synchronized
    fun setDiscardAllowed(companion: Boolean, wanted: Boolean, now: Instant, wait: Duration, sessionRunning: Boolean): LunchDiscardOutcome {
        val (changed, outcome) = discardSetting(companion).change(wanted, now, wait, sessionRunning)
        saveDiscard(companion, changed)
        return outcome
    }

    /** A waiting "on" that waited long enough takes effect in both modes' copies, only while no session runs. */
    @Synchronized
    fun settleDiscard(now: Instant, sessionRunning: Boolean) {
        for (companion in listOf(false, true)) {
            val current = discardSetting(companion)
            val settled = current.applyIfReady(now, sessionRunning)
            if (settled != current) saveDiscard(companion, settled)
        }
    }

    private fun saveDiscard(companion: Boolean, setting: LunchDiscardSetting) {
        val editor = settingsPrefs.edit().putBoolean(discardKey(companion, "allowed"), setting.allowed)
        val pending = setting.pending
        val pendingFrom = setting.pendingFrom
        if (pending != null) editor.putBoolean(discardKey(companion, "pending"), pending) else editor.remove(discardKey(companion, "pending"))
        if (pendingFrom != null) editor.putString(discardKey(companion, "pendingFrom"), pendingFrom.toString()) else editor.remove(discardKey(companion, "pendingFrom"))
        editor.apply()
    }

    private fun discardKey(companion: Boolean, part: String): String = LunchDiscardSetting.prefKey(companion, part)

    /** Settings → Your day → "Lunch length": 15-120 minutes in steps of 5; the next lunch takes it. */
    fun setLength(minutes: Int) {
        val clamped = clampLength(Math.round(minutes / LENGTH_STEP.toDouble()).toInt() * LENGTH_STEP)
        if (clamped == _lengthMinutes.value) return
        settingsPrefs.edit().putInt(LENGTH_KEY, clamped).apply()
        // This phone's own setting: nothing to sync; the watch hears of it through [lengthMinutes].
        _lengthMinutes.value = clamped
    }

    /**
     * A lunch from device [sender]'s file: taken when it's today's and its edit is newer than ours. True when it was taken: [lunch] and
     * the watch's flow follow at once, the "Lunch over!" alarm is set again by [save], and the caller redraws the notification and widget.
     * A record identical to the one last received from that sender is ignored: the files are read again and again, and a clamped stamp would
     * otherwise be re-clamped to the newer "now" and beat an edit made here in between.
     */
    @Synchronized
    fun receive(sender: java.util.UUID, received: SyncLunch, now: Instant): Boolean {
        val lunch = receiving.take(sender, received, today(now), now) { isToday(it, now) } ?: return false
        save(lunch, overTold = now >= lunch.end, now = now)
        return true
    }

    /** Today's lunch for the watch's Lunch page: shown on a workday (or a day with a flexible break), or while lunch runs. */
    fun forWatch(plan: DayPlan, now: Instant): WatchLunch {
        val date = now.atZone(ZoneId.systemDefault()).toLocalDate()
        val taken = today(now)
        return WatchLunch(
            workday = plan.isWorkday(date) || plan.flexibleBreakOn(date) != null,
            lengthMinutes = _lengthMinutes.value,
            start = taken?.start,
            end = taken?.end,
        )
    }

    /** "Lunch over!" is due and not told yet. */
    fun overDue(now: Instant): Boolean = today(now)?.let { now >= it.end } == true && !prefs.getBoolean("overTold", false)

    fun markOverTold() {
        prefs.edit().putBoolean("overTold", true).apply()
    }

    /** The hint went out on [day]. */
    fun hintedOn(day: LocalDate): Boolean = prefs.getString("hintedOn", null) == day.toString()

    fun markHinted(day: LocalDate) {
        prefs.edit().putString("hintedOn", day.toString()).apply()
    }

    private fun save(lunch: SyncLunch, overTold: Boolean, now: Instant) {
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
        // Split lunch (ADR 88): the day's allowance, the counted pieces and the discarded mark (the sync file's own JSON for the pieces).
        val allowance = lunch.allowance
        if (allowance != null) editor.putInt("allowance", allowance) else editor.remove("allowance")
        if (lunch.counted != null) editor.putString("counted", SyncFileJson.writeLunch(lunch).getValue("counted").toString()) else editor.remove("counted")
        editor.putBoolean("discarded", lunch.discarded)
        editor.apply()
        scheduleOver(lunch, overTold, now)
    }

    // The "Lunch over!" alarm at its end (inexact but allowed while idle: no special permission).
    private fun scheduleOver(lunch: SyncLunch, overTold: Boolean, now: Instant) {
        val alarms = context.getSystemService(AlarmManager::class.java) ?: return
        val intent = PendingIntent.getBroadcast(
            context, 52, Intent(context, LunchReceiver::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        alarms.cancel(intent)
        if (!overTold && lunch.end.isAfter(now)) {
            alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, lunch.end.toEpochMilli(), intent)
        }
    }

    private fun load(): SyncLunch? {
        val start = prefs.getString("start", null)?.let { runCatching { Instant.parse(it) }.getOrNull() } ?: return null
        val end = prefs.getString("end", null)?.let { runCatching { Instant.parse(it) }.getOrNull() } ?: return null
        val editedAt = prefs.getString("editedAt", null)?.let { runCatching { Instant.parse(it) }.getOrNull() }
        val allowance = if (prefs.contains("allowance")) prefs.getInt("allowance", 0) else null
        val counted = prefs.getString("counted", null)?.let { text ->
            runCatching {
                val lunch = SyncFileJson.readLunch(buildJsonObject {
                    put("start", JsonPrimitive(start.toString()))
                    put("end", JsonPrimitive(end.toString()))
                    put("counted", Json.parseToJsonElement(text))
                })
                lunch.counted
            }.getOrNull()
        }
        return SyncLunch(start, end, editedAt, allowance, counted, prefs.getBoolean("discarded", false))
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
