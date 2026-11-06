package com.accountabilibuddy.companion.nudge

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.DayWorkWindows
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.shared.sync.SyncWorkEndedWindow
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * "I've finished work for the day" (ADR 67): the rest of today's guarded work ends now, on this phone at once and on the PCs after
 * their own looser-change delay (the phone's `workEnded` in its sync file). "Today" is the plan date, which runs until 4:00 AM,
 * like a PC's Start shutdown (ADR 65).
 */
object FinishedWork {
    /** Hours past midnight the plan date still counts as the day before (the PC's ShutdownService.PlanDateOf). */
    private const val NEW_DAY_HOUR = 4L

    /** Ends what is left of today's guarded work from [now]; false when no work was left to end. */
    fun finish(app: CompanionApp, now: Instant = Instant.now()): Boolean {
        val zone = ZoneId.systemDefault()
        val local = LocalDateTime.ofInstant(now, zone)
        val planDate = local.minusHours(NEW_DAY_HOUR).toLocalDate()
        val planDayEnd = planDate.plusDays(1).atStartOfDay().plusHours(NEW_DAY_HOUR)
        val windows = DayWorkWindows.workWindows(app.effectivePlan(), LocalWindow(local, planDayEnd), local)
            .map { it.window }
            .filter { it.end.isAfter(local) }
            .map { window ->
                SyncWorkEndedWindow(
                    (if (window.start.isAfter(local)) window.start else local).atZone(zone).toInstant(),
                    window.end.atZone(zone).toInstant(),
                )
            }
        if (windows.isEmpty()) {
            return false
        }

        app.phoneWorkEnded.end(planDate, windows, now)
        return true
    }
}
