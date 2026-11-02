package com.accountabilibuddy.companion.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.engine.DayWorkWindows
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.notify.HeaderSource
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.sync.PlanWait
import com.accountabilibuddy.companion.ui.plan.PlanDayCell
import com.accountabilibuddy.companion.ui.plan.PlanDayNames
import com.accountabilibuddy.companion.ui.plan.PlanPane
import com.accountabilibuddy.companion.ui.plan.PlanPaneActions
import com.accountabilibuddy.companion.ui.plan.PlanPaneModel
import com.accountabilibuddy.companion.ui.plan.PlanTimelineBuilder
import kotlinx.coroutines.delay
import java.time.DayOfWeek
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.TextStyle
import java.time.temporal.ChronoUnit
import java.time.temporal.TemporalAdjusters
import java.util.Locale

/**
 * Plan (board 5c): the host of the redesigned pane. It reads the plan, the calendar and today's focus, keeps which day is
 * picked (in memory and across rotation), works out what to say about that day and gives it to [PlanPane]. Every change goes
 * out through the lambdas, which open the editors or send the edit to the PC exactly as before ([canChange] is false with no
 * PC to take it, so the page is read-only and says why). Nothing here starts, ends or unlocks anything.
 */
@Composable
fun PlanScreen(
    state: CompanionState,
    model: DayBarModel,
    clock: ClockFormat,
    /** The PC that takes the plan edits (ADR 42); null when none is linked. */
    pcName: String?,
    canChange: Boolean,
    /** Set while a focus session runs: a plan edit saved now waits for it to end. */
    guardsLock: GuardsLock?,
    /** A plan edit a PC took and is holding (ADR 28); null for none. */
    planWait: PlanWait?,
    /** "Change a day" (ADR 37). */
    onChangeDay: () -> Unit,
    /** "Templates": the day plan editor (ADR 19). */
    onTemplates: () -> Unit,
    onCategories: () -> Unit,
    /** "This is week N of M" (ADR 47): picks the week of the cycle as a plan edit. */
    onSetWeek: (week: Int) -> Unit,
    onDiscardPlanEdit: () -> Unit,
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    // Looked at again every half minute, so the "Now" rule moves and a day that ends is no longer today.
    val now by produceState(LocalDateTime.now()) {
        while (true) {
            delay(30_000)
            value = LocalDateTime.now()
        }
    }
    val today = now.toLocalDate()
    var pickedDay by rememberSaveable { mutableLongStateOf(today.toEpochDay()) }
    val selected = LocalDate.ofEpochDay(pickedDay)
    val plan = state.document.dayPlan

    val minute = now.truncatedTo(ChronoUnit.MINUTES)
    val focus = remember(model, minute) { HeaderSource.focus(app, now) }
    val timeline = remember(plan, selected, minute, state.events, focus, model.today) {
        val zone = ZoneId.systemDefault()
        PlanTimelineBuilder.build(
            plan = plan, date = selected, now = now, events = state.events,
            focused = DayWorkWindows.focusedToday(focus, now, zone)?.invoke(selected) ?: Duration.ZERO,
            goal = Duration.ofMinutes((model.today?.goalMinutes ?: 0).toLong()),
            nightFallback = state.sleep::fallbackNightBefore, clock = clock,
        )
    }

    val weekStart = selected.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY))
    val cells = (0L until 7L).map { offset ->
        val date = weekStart.plusDays(offset)
        PlanDayCell(
            date = date, weekday = date.dayOfWeek.getDisplayName(TextStyle.SHORT, Locale.US), number = date.dayOfMonth.toString(),
            type = PlanDayNames.short(plan, date), isToday = date == today, isSelected = date == selected,
        )
    }

    val weeks = plan.cycle.weeks.size
    val holding = planWait?.takeIf { it.appliesAt.isAfter(Instant.now()) }
    val paneModel = PlanPaneModel(
        title = "${selected.dayOfWeek.getDisplayName(TextStyle.FULL, Locale.US)} · ${PlanDayNames.full(plan, selected)}",
        cells = cells,
        timeline = timeline,
        modeLine = modeLine(state, model, selected, today, timeline.hasWork),
        readOnlyNote = when {
            !canChange -> "Read-only: this phone can't change the plan because no PC is linked to take the edit. Link one in Settings › Devices & sync."
            selected.isBefore(today) -> "This day is over, so it can't be changed."
            else -> null
        },
        lockNote = guardsLock?.takeIf { canChange }?.let {
            "A focus session is on until ${it.untilText() ?: "it ends"}. A plan change you save now waits for ${pcName ?: "the PC"} to finish it."
        },
        waitNote = holding?.let { "${it.pcName} is holding your change and applies it at about ${CLOCK.format(it.appliesAt.atZone(ZoneId.systemDefault()))}." },
        pending = state.pendingPlan,
        weekLabel = if (weeks > 1) "Week ${plan.cycle.weekNumberFor(selected)} of $weeks" else null,
        weekCount = weeks,
        weekNumber = plan.cycle.weekNumberFor(today),
        isToday = selected == today,
        canChange = canChange,
    )

    PlanPane(
        model = paneModel,
        actions = PlanPaneActions(
            onPick = { cell -> pickedDay = cell.date.toEpochDay() },
            onShiftWeek = { by -> pickedDay = selected.plusWeeks(by.toLong()).toEpochDay() },
            onToday = { pickedDay = today.toEpochDay() },
            onChangeDay = onChangeDay,
            onTemplates = onTemplates,
            onCategories = onCategories,
            onSetWeek = onSetWeek,
            onDiscardEdit = onDiscardPlanEdit,
        ),
    )
}

private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

// How a day reads when it isn't an ordinary schedule (ADR 47), in the words of Today's headline: a Budget day says what is left
// (today) or the hours it holds, a Free day says nothing is guarded, a day with no work blocks is a day off.
private fun modeLine(state: CompanionState, model: DayBarModel, date: LocalDate, today: LocalDate, hasWork: Boolean): String? {
    val template = state.document.dayPlan.templateFor(date)
    return when (template.mode) {
        DayMode.Free -> "Free day. Nothing is guarded."
        DayMode.Budget ->
            if (date == today) model.today?.next ?: "Budget day."
            else "Budget day: ${hoursText(template.budgetMinutes ?: 0)} of focus."
        DayMode.Schedule -> if (hasWork) null else "Day off."
    }
}
