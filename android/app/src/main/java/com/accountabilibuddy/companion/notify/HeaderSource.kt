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
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import com.accountabilibuddy.companion.watch.PhoneSnapshot
import com.accountabilibuddy.shared.SnapshotBuilder
import com.accountabilibuddy.shared.WatchSnapshot
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Gathers what the context-aware header needs from the app ([DayHeader]): today's plan parts, events and night, the
 * combined focus of the user's devices (the linked PC's day and session, the other devices' sync files) and the
 * phone's screen time.
 */
object HeaderSource {
    fun header(app: CompanionApp, now: LocalDateTime, clock: ClockFormat, phoneMinutes: Int?): HeaderText {
        val state = app.store.state.value
        val zone = ZoneId.systemDefault()
        val plan = state.document.dayPlan
        val start = now.toLocalDate().atStartOfDay()
        val night = if (state.sleep.enabled) state.sleep.toSchedule(plan).nightAround(now) else null
        val input = HeaderInput(
            now, zone, plan.partsIn(LocalWindow(start, start.plusDays(1))), state.events, night, state.sleep.goal,
            wakeSource = null, focus = focus(app, state, now, zone), phoneMinutes = phoneMinutes,
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

    // Null while no device has said anything about today's focus (no PC paired, nothing received).
    private fun focus(app: CompanionApp, state: CompanionState, now: LocalDateTime, zone: ZoneId): WatchSnapshot? {
        val received = app.sync.received.value.map { it.file.device.name to it.file }
        val pc = app.link.pairedPc.value
        if (state.focusDay == null && received.isEmpty() && pc == null) {
            return null
        }

        val last = app.link.lastState.value
        return SnapshotBuilder.build(
            PhoneSnapshot.input(
                state, app.link.status.value, linked = true, pc?.pcName, now.atZone(zone).toInstant(), zone,
                lastState = last?.state, lastStateAt = last?.at, received = received,
            ),
        )
    }
}
