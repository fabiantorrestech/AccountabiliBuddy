package com.accountabilibuddy.companion.nudge

import android.content.Context
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/** One use of "I need my phone for a bit": why, from when, for how long. */
data class EmergencyUse(val reason: String, val start: Instant, val minutes: Int) {
    val end: Instant get() = start.plus(Duration.ofMinutes(minutes.toLong()))
}

/**
 * "I need my phone for a bit" (inside "Not working today" and on the parked notice): a reason and 5–60 minutes
 * without the work card or nudges, from a daily allowance (45 minutes by default), shown apart from pickups on the PC.
 * (The always-allowed apps are the essentials, [com.accountabilibuddy.companion.essentials.EssentialsStore].) Prefs "emergency".
 */
class EmergencyStore(context: Context) {
    private val prefs = context.getSharedPreferences("emergency", Context.MODE_PRIVATE)

    /** Minutes a day; 45 to start. */
    var dailyMinutes: Int
        get() = prefs.getInt(KEY_DAILY, DEFAULT_DAILY)
        set(value) = prefs.edit().putInt(KEY_DAILY, value.coerceIn(0, 240)).apply()


    /** Today's uses (older days are dropped as they're read). */
    fun usesOn(date: LocalDate, zone: ZoneId = ZoneId.systemDefault()): List<EmergencyUse> =
        all().filter { it.start.atZone(zone).toLocalDate() == date }

    fun minutesLeft(date: LocalDate, now: Instant = Instant.now()): Int =
        (dailyMinutes - usesOn(date).sumOf { use -> if (use.end.isAfter(now)) Duration.between(use.start, now).toMinutes().toInt() else use.minutes })
            .coerceAtLeast(0)

    /** The use under way, or null. */
    fun active(now: Instant = Instant.now()): EmergencyUse? = all().lastOrNull { !now.isBefore(it.start) && now.isBefore(it.end) }

    fun isActive(now: Instant = Instant.now()): Boolean = active(now) != null

    /** Starts a use; refused (false) beyond what's left today. */
    fun start(reason: String, minutes: Int, now: Instant = Instant.now()): Boolean {
        if (minutes <= 0 || minutes > minutesLeft(LocalDate.now(), now)) {
            return false
        }

        save(all().filter { it.start.isAfter(now.minus(Duration.ofDays(15))) } + EmergencyUse(reason, now, minutes))
        return true
    }

    /** Back early: the use ends now (only the time used counts). */
    fun endNow(now: Instant = Instant.now()) {
        val current = active(now) ?: return
        val used = Duration.between(current.start, now).toMinutes().toInt().coerceAtLeast(1)
        save(all().map { if (it == current) it.copy(minutes = used) else it })
    }

    /** Every use of the last 15 days (for pickups during the other devices' sessions). */
    fun all(): List<EmergencyUse> = prefs.getStringSet(KEY_USES, emptySet()).orEmpty().mapNotNull { line ->
        val parts = line.split(SEPARATOR, limit = 3)
        if (parts.size != 3) return@mapNotNull null
        runCatching { EmergencyUse(parts[2], Instant.parse(parts[0]), parts[1].toInt()) }.getOrNull()
    }.sortedBy { it.start }

    private fun save(uses: List<EmergencyUse>) {
        prefs.edit().putStringSet(KEY_USES, uses.map { "${it.start}$SEPARATOR${it.minutes}$SEPARATOR${it.reason}" }.toSet()).apply()
    }

    companion object {
        const val DEFAULT_DAILY = 45
        val LENGTHS = listOf(5, 15, 30, 60)
        val REASONS = listOf("Interview or call", "Urgent message", "Directions")
        private const val KEY_DAILY = "dailyMinutes"
        private const val KEY_USES = "uses"
        private const val SEPARATOR = "|"

    }
}
