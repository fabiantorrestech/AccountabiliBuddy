package com.accountabilibuddy.companion.nudge

import android.content.Context
import java.time.Instant
import java.time.LocalDate

/** [NudgeState] in preferences "takeover". */
class NudgeStore(context: Context) {
    private val prefs = context.getSharedPreferences("takeover", Context.MODE_PRIVATE)

    @Synchronized
    fun load(): NudgeState = NudgeState(
        takeoverOn = prefs.getBoolean("on", true),
        lastShown = instant("lastShown"),
        snoozedUntil = instant("snoozedUntil"),
        startingUntil = instant("startingUntil"),
        snoozeDay = prefs.getString("snoozeDay", null)?.let { runCatching { LocalDate.parse(it) }.getOrNull() },
        snoozes = prefs.getInt("snoozes", 0),
        dayOffAskedAt = instant("dayOffAskedAt"),
        dayOff = prefs.getString("dayOff", null)?.let { runCatching { LocalDate.parse(it) }.getOrNull() },
        unanswered = prefs.getBoolean("unanswered", false),
    )

    @Synchronized
    fun save(state: NudgeState) {
        prefs.edit()
            .putBoolean("on", state.takeoverOn)
            .putString("lastShown", state.lastShown?.toString())
            .putString("snoozedUntil", state.snoozedUntil?.toString())
            .putString("startingUntil", state.startingUntil?.toString())
            .putString("snoozeDay", state.snoozeDay?.toString())
            .putInt("snoozes", state.snoozes)
            .putString("dayOffAskedAt", state.dayOffAskedAt?.toString())
            .putString("dayOff", state.dayOff?.toString())
            .putBoolean("unanswered", state.unanswered)
            .apply()
    }

    fun update(change: (NudgeState) -> NudgeState) = save(change(load()))

    private fun instant(key: String): Instant? = prefs.getString(key, null)?.let { runCatching { Instant.parse(it) }.getOrNull() }
}
