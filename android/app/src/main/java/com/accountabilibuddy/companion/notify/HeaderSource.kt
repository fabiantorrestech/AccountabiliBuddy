package com.accountabilibuddy.companion.notify

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.DayHeader
import com.accountabilibuddy.companion.engine.DayTimeline
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
        val plan = state.document.dayPlan
        val start = now.toLocalDate().atStartOfDay()
        val schedule = state.sleep.toSchedule(plan)
        val night = if (state.sleep.enabled) schedule.nightAround(now) else null
        val byAlarm = night != null && schedule.wakesByAlarm(night.end.toLocalDate().minusDays(1))
        val input = HeaderInput(
            now, zone, plan.partsIn(LocalWindow(start, start.plusDays(1))), state.events, night, state.sleep.goal,
            wakeSource = if (byAlarm) "alarm" else null, focus = focus, phoneMinutes = phoneMinutes,
            // The clock now, not [now]: Today's [now] may be up to 30 s old when a test starts.
            testEndsAt = app.testSession.running()?.second,
        )
        return DayHeader.build(input, clock)
    }

    /** Today's phone screen time in minutes over the bar's day; null without Usage access. */
    fun phoneMinutes(context: Context, state: CompanionState, now: LocalDateTime, builder: DayBarBuilder): Int? {
        val reader = PhoneUsageReader(context)
        if (!reader.hasAccess()) {
            return null
        }

        val day = builder.dayAt(now, state.dayBar, state.sleep, state.document.dayPlan)
        val focus = DayTimeline.planned(state.document.dayPlan, day).map { it.window }
        return runCatching { reader.read(day, focus).total.toMinutes().toInt() }.getOrNull()
    }

    /** The devices' combined focus today; null while no device has said anything about it (nothing received). */
    fun focus(app: CompanionApp, now: LocalDateTime): WatchSnapshot? {
        val state = app.store.state.value
        val zone = ZoneId.systemDefault()
        val received = app.sync.received.value.map { it.file.device.name to it.file }
        if (received.isEmpty()) {
            return null
        }

        return SnapshotBuilder.build(
            PhoneSnapshot.input(
                state, linked = true, PcFileControl.target(app.sync.received.value, app.pcRemote.chosenPcId.value)?.file?.device?.name, now.atZone(zone).toInstant(), zone,
                received = received,
            ),
        )
    }
}
