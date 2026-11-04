package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.engine.TodayInfo
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.steps.StepsState
import com.accountabilibuddy.companion.sync.BeeminderGoals
import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.companion.today.TodayItem
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.companion.usage.PhoneUsage

/** Everything the arrangeable parts of Today draw from. Built by [TodayPane]; the parts decide for themselves whether they have anything to show. */
class TodayItemContext(
    val inSession: Boolean,
    val state: CompanionState,
    val model: DayBarModel,
    /** Today's line of the day bar model (on a phone with no plan yet, worded for that). */
    val info: TodayInfo?,
    val clock: ClockFormat,
    val pc: TodayPc,
    val comebacks: OpenComebacks?,
    val phoneUsage: PhoneUsage?,
    val hasTimer: Boolean,
    val lunchChip: LunchChipKind?,
    val lunch: TodayLunch,
    val guardsLock: GuardsLock?,
    val guardOn: Boolean,
    val beeminder: BeeminderGoals?,
    val noiseCard: (@Composable () -> Unit)?,
    val stepsState: StepsState,
    val actions: TodayActions,
    /** A session with a task list whose every task is ticked. */
    val allTasksTicked: Boolean,
    val showWorkUse: Boolean,
    val showDayOff: Boolean,
)

/**
 * Draws one arrangeable part of Today, or nothing when it has nothing to say (the same conditions the cards always had). A part
 * only decides what it draws; where it sits and whether it shows at all is the layout's say ([com.accountabilibuddy.companion.today.TodayLayout]).
 * A few parts carry a neighbour that belongs with them: the guard banner brings the calendar lines (nothing running), the lunch
 * chip brings the "all tasks are ticked" line (a session), the focus ring brings the Budget pace (a session).
 */
fun LazyListScope.todayItem(part: TodayItem, c: TodayItemContext) {
    when (part) {
        TodayItem.Guard -> {
            if (c.guardsLock != null) item(key = "guard") { GuardBanner(c.guardsLock, c.guardOn) }
            // ADR 58: what calendar events changed in the guards today, by category and time, never a title.
            if (!c.inSession) c.info?.calendar?.forEach { line -> item(key = "calendar-$line") { MutedLine(line) } }
        }
        TodayItem.LunchChip -> {
            // Every task ticked: the phone's way out is still the PC's wait; the PC can end it at once.
            if (c.inSession && c.allTasksTicked) item(key = "all-done") { MutedLine("All tasks are ticked. On the PC, a finished session can usually end at once.") }
            if (c.lunchChip != null) item(key = "lunch-chip") { LunchChip(c.lunch, c.clock, c.lunchChip) }
        }
        TodayItem.PcMissing -> if (!c.inSession && !c.pc.hasPc) item(key = "pc-missing") { PcMissingCard(c.actions.onOpenDevices) }
        TodayItem.Ring -> {
            val info = c.info
            if (info != null && (info.goalMinutes != null || info.focusedMinutes > 0)) item(key = "ring") { RingCard(info.focusedMinutes, info.goalMinutes) }
            // The Budget pace the PC's own card showed (ADR 47); amber-ish (the waiting colour) when the day can't hold the rest.
            if (c.inSession) info?.pace?.let { pace -> item(key = "pace") { Text(BudgetPaceLine.text(pace), style = Ab.type.bodyMedium, color = if (pace.behind) Ab.meaning(Ab.colors.waiting) else Ab.colors.muted) } }
        }
        TodayItem.Comebacks -> if (!c.inSession && c.comebacks != null && c.comebacks.items.isNotEmpty()) item(key = "comebacks") { ComebackCard(c.comebacks, c.actions.onOpenTasks) }
        TodayItem.SmallCards -> if (!c.inSession) item(key = "small") { SmallCardsRow(c.hasTimer, c.phoneUsage, c.clock, c.actions.onAllowUsage) }
        TodayItem.Noise -> c.noiseCard?.let { card -> item(key = "noise") { card() } }
        TodayItem.DailySteps -> if (c.stepsState.enabled) item(key = "daily-steps") { DailyStepsCard(c.stepsState, c.clock) }
        TodayItem.DayBar -> item(key = "more-daybar") { DayBarCard(c.model) }
        TodayItem.WorkUse -> if (c.showWorkUse) item(key = "more-workuse") { WorkUseCard() }
        TodayItem.LunchCard -> {
            val lunch = c.lunch
            if (lunch.workday || lunch.flex != null || lunch.taken != null) item(key = "more-lunch") { LunchCard(lunch, c.clock) }
        }
        TodayItem.Beeminder -> c.beeminder?.let { goals -> item(key = "more-beeminder") { BeeminderCard(goals, c.clock) } }
        TodayItem.Timers -> item(key = "more-timers") {
            val app = LocalContext.current.applicationContext as CompanionApp
            TimersCard(app.timers, c.clock)
        }
        TodayItem.DayOff -> if (c.showDayOff) item(key = "more-dayoff") { DayOffCard() }
        TodayItem.Import -> if (!c.state.hasImport) item(key = "more-import") { ImportCard(c.actions.onImport) }
        // The test session cannot start while a real one runs, so it is not offered then.
        TodayItem.TestSession -> if (!c.inSession) item(key = "more-test") { TestSessionCard() }
    }
}
