package com.accountabilibuddy.companion.notify

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.DayHeader
import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.engine.DayWorkWindows
import com.accountabilibuddy.companion.engine.HeaderInput
import com.accountabilibuddy.companion.engine.HeaderText
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import com.accountabilibuddy.companion.watch.PhoneSnapshot
import com.accountabilibuddy.shared.SnapshotBuilder
import com.accountabilibuddy.shared.WatchSnapshot
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Gathers what the context-aware header needs from the app ([DayHeader]): today's plan parts, events and night, the
 * combined focus of the user's devices (their sync files: sessions and the PC's running session) and the phone's
 * screen time.
 */
object HeaderSource {
    fun header(app: CompanionApp, now: LocalDateTime, clock: ClockFormat, phoneMinutes: Int?, focus: WatchSnapshot? = focus(app, now)): HeaderText {
        val state = app.store.state.value
        val zone = ZoneId.systemDefault()
        val plan = app.effectivePlan(state)
        val start = now.toLocalDate().atStartOfDay()
        val schedule = state.sleep.toSchedule(plan)
        val night = if (state.sleep.enabled) schedule.nightAround(now) else null
        val byAlarm = night != null && schedule.wakesByAlarm(night.end.toLocalDate().minusDays(1))
        val input = HeaderInput(
            now, zone, DayWorkWindows.partsIn(plan, LocalWindow(start, start.plusDays(1)), now, DayWorkWindows.focusedToday(focus, now, zone), state.sleep::fallbackNightBefore), state.events, night, state.sleep.goal,
            wakeSource = if (byAlarm) "alarm" else null, focus = focus, phoneMinutes = phoneMinutes,
            // The clock now, not [now]: Today's [now] may be up to 30 s old when a test starts.
            testEndsAt = app.testSession.running()?.second,
            // Today's lunch while it runs (the clock now, like the test session above); it follows +5 / -5 and a lunch from another device.
            // Not while a session is counting (see [sessionCounting]): then the session's own countdown is the right one.
            lunchEndsAt = app.lunch.today()?.end?.takeIf { it.isAfter(java.time.Instant.now()) && !sessionCounting(app, java.time.Instant.now()) },
        )
        return DayHeader.build(input, clock)
    }

    /**
     * A session that is really counting now: the phone's test session, or a PC file's live running session that is not stepped away
     * or off ([PcFileControl.liveRunning] reports a PC that is off as away). Lunch pauses a session as a step away, so a session
     * that is paused during lunch does not count; one started during lunch, or back early from the step-away shortcut, does.
     */
    private fun sessionCounting(app: CompanionApp, now: java.time.Instant): Boolean {
        if (app.testSession.running(now) != null) return true
        return app.sync.received.value.any { received ->
            received.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC &&
                PcFileControl.liveRunning(received.file, now)?.let { it.awaySince == null } == true
        }
    }

    /** Today's phone screen time in minutes over the bar's day; null without Usage access. */
    fun phoneMinutes(context: Context, state: CompanionState, now: LocalDateTime, builder: DayBarBuilder): Int? {
        val reader = PhoneUsageReader(context)
        if (!reader.hasAccess()) {
            return null
        }

        val plan = (context.applicationContext as CompanionApp).effectivePlan(state)
        val day = builder.dayAt(now, state.dayBar, state.sleep, plan)
        val focus = DayWorkWindows.workWindows(plan, day, now, nightFallback = state.sleep::fallbackNightBefore).map { it.window }
        return runCatching { reader.read(day, focus).total.toMinutes().toInt() }.getOrNull()
    }

    /** The devices' combined focus today; null while no device has said anything about it (nothing received). */
    fun focus(app: CompanionApp, now: LocalDateTime): WatchSnapshot? {
        val state = app.store.state.value
        val zone = ZoneId.systemDefault()
        val received = app.sync.received.value.map { it.file.device.name to it.file }
        val totals = app.sync.allReceived.value.map { it.file.device.name to it.file }
        if (totals.isEmpty()) {
            return null
        }

        return SnapshotBuilder.build(
            PhoneSnapshot.input(
                state, linked = true, PcFileControl.target(app.sync.received.value, app.pcRemote.chosenPcId.value)?.file?.device?.name, now.atZone(zone).toInstant(), zone,
                received = received, totals = totals, plan = app.effectivePlan(state),
            ),
        )
    }
}
