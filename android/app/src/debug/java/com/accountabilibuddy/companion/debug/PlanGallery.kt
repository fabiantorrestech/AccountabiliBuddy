package com.accountabilibuddy.companion.debug

import android.content.Intent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.TodayInfo
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.DayBarSettings
import com.accountabilibuddy.companion.model.PendingDayPlan
import com.accountabilibuddy.companion.model.PendingStatus
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.sync.PlanWait
import com.accountabilibuddy.companion.ui.PlanScreenBody
import com.accountabilibuddy.companion.ui.kit.ChangeWaitsSheet
import com.accountabilibuddy.companion.ui.plan.PlanWaitSheet
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * Debug only: the Plan page at a fixed "now" (1:38 PM today) over made-up data, for screenshots on the emulator; the real clock is
 * whatever the emulator says. Opened through [KitGalleryActivity] with `--es show plan` and:
 * `--es kind week|budget|free|dayoff` (the plan, see [DemoPcFile.dayPlan]), `--ez pc false` (no PC: read-only), `--ez locked true`
 * (a focus session runs), `--es pending waiting|locked|invalid|update` (an edit that hasn't reached the PC), `--ez wait true`
 * (a PC is holding an edit: the sheet), `--ez confirm true` (board 5h's "Queue it" sheet).
 */
@Composable
fun PlanGallery(intent: Intent) {
    val kind = intent.getStringExtra("kind") ?: "week"
    val hasPc = intent.getBooleanExtra("pc", true)
    val locked = intent.getBooleanExtra("locked", false)
    val clock = ClockFormat(is24Hour = false)
    val today = LocalDate.now()
    val now = today.atTime(13, 38)
    val plan = DemoPcFile.dayPlan(kind)
    val events = listOf(
        CalendarEvent("demo-1", "demo", "Design review", today.atTime(10, 0), today.atTime(10, 45), false),
        CalendarEvent("demo-2", "demo", "Standup", today.atTime(14, 30), today.atTime(15, 0), false),
        CalendarEvent("demo-3", "demo", "Team offsite", today.plusDays(1).atStartOfDay(), today.plusDays(2).atStartOfDay(), true),
    )
    val sleep = SleepSettings.Default
    val document = ProfileDocument.Empty.copy(dayPlan = plan)
    val bar = DayBarBuilder(clock).build(now, document, events, DayBarSettings.Default, sleep)
    val info = TodayInfo.of(plan, now, null, clock, sleep::fallbackNightBefore, false).copy(focusedMinutes = 70, goalMinutes = 120)
    val pending = when (intent.getStringExtra("pending")) {
        "waiting" -> PendingDayPlan("1", plan, plan, Instant.now(), PendingStatus.Waiting)
        "locked" -> PendingDayPlan("1", plan, plan, Instant.now(), PendingStatus.Locked)
        "invalid" -> PendingDayPlan("1", plan, plan, Instant.now(), PendingStatus.Invalid, "Work overlaps lunch on Tuesday")
        "update" -> PendingDayPlan("1", plan, plan, Instant.now(), PendingStatus.NeedsPcUpdate)
        else -> null
    }
    val zone = ZoneId.systemDefault()
    val wait = if (intent.getBooleanExtra("wait", false)) PlanWait("Work PC", 10, now.plusMinutes(10).atZone(zone).toInstant()) else null
    val until = if (locked) now.plusMinutes(42).atZone(zone).toInstant() else null
    var sheet by remember { mutableStateOf(wait != null) }
    var confirm by remember { mutableStateOf(intent.getBooleanExtra("confirm", false)) }

    Column(Modifier.fillMaxSize().background(Ab.colors.bg).safeDrawingPadding()) {
        PlanScreenBody(
            state = CompanionState(document = document, events = events, sleep = sleep, pendingPlan = pending),
            model = bar.copy(today = info),
            clock = clock,
            now = now,
            focusedToday = Duration.ofMinutes(70),
            pcName = if (hasPc) "Work PC" else null,
            canChange = hasPc,
            guardsLock = until?.let { GuardsLock(it) },
            planWait = wait,
            onChangeDay = {}, onTemplates = {}, onCategories = {}, onSetWeek = {}, onDiscardPlanEdit = {},
        )
    }

    if (wait != null && sheet) PlanWaitSheet(wait, onDismiss = { sheet = false })
    if (confirm) {
        // Board 5h: asked before the wait starts (the shape the guard and sleep switches reuse).
        ChangeWaitsSheet(
            title = "Move bedtime to 12:30 AM?",
            body = "You're mid-session, so this waits 10 minutes. If you still want it then, it applies on its own.",
            appliesAt = now.plusMinutes(10).atZone(zone).toInstant(),
            onQueue = {},
            onDismiss = { confirm = false },
        )
    }
}
