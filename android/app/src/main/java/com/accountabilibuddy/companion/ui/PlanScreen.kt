package com.accountabilibuddy.companion.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
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
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.TextStyle
import java.time.temporal.ChronoUnit
import java.time.temporal.TemporalAdjusters
import java.util.Locale

/**
 * Plan (board 5c): the host of the redesigned pane. It keeps the clock (looked at again every half minute, only while the app is in
 * front) and today's counted focus, and hands them to [PlanScreenBody]. Nothing here starts, ends or unlocks anything.
 */
@Composable
fun PlanScreen(
    state: CompanionState,
    model: DayBarModel,
    clock: ClockFormat,
    /** The PC that takes the plan edits (ADR 42); null when none is linked. */
    pcName: String?,
    canChange: Boolean,
    /** Set while a focus session runs: the plan rests (a test session too, like the guard settings). */
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
    // Looked at again every half minute while the app is in front (and at once when it comes back), so the "Now" rule moves and
    // a day that ends is no longer today.
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var now by remember { mutableStateOf(LocalDateTime.now()) }
    LaunchedEffect(lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                now = LocalDateTime.now()
                delay(30_000)
            }
        }
    }

    val minute = now.truncatedTo(ChronoUnit.MINUTES)
    val focus = remember(model, minute) { HeaderSource.focus(app, now) }
    val zone = ZoneId.systemDefault()
    PlanScreenBody(
        state, model, clock, now, DayWorkWindows.focusedToday(focus, now, zone)?.invoke(now.toLocalDate()) ?: Duration.ZERO,
        pcName, canChange, guardsLock, planWait, onChangeDay, onTemplates, onCategories, onSetWeek, onDiscardPlanEdit,
    )
}

/**
 * The page for a given [now] and today's counted focus [focusedToday] (a Budget day's windows and the "done" times use it; other days have none): works
 * out which day is picked, what to say about it and gives it to [PlanPane]. Every change goes out through the lambdas, which open
 * the editors or send the edit to the PC exactly as before. The buttons that change the plan are off with no PC to take the edit
 * ([canChange]) and while a focus session holds the plan ([guardsLock]); the page says why in both cases. The debug gallery draws it
 * at a fixed time.
 */
@Composable
internal fun PlanScreenBody(
    state: CompanionState,
    model: DayBarModel,
    clock: ClockFormat,
    now: LocalDateTime,
    focusedToday: Duration,
    pcName: String?,
    canChange: Boolean,
    guardsLock: GuardsLock?,
    planWait: PlanWait?,
    onChangeDay: () -> Unit,
    onTemplates: () -> Unit,
    onCategories: () -> Unit,
    onSetWeek: (week: Int) -> Unit,
    onDiscardPlanEdit: () -> Unit,
) {
    val today = now.toLocalDate()
    // Which day is picked survives rotation. "Follow today" (the default, and after picking today) moves on at midnight by itself.
    var pickedDay by rememberSaveable { mutableLongStateOf(FOLLOW_TODAY) }
    val selected = if (pickedDay == FOLLOW_TODAY) today else LocalDate.ofEpochDay(pickedDay)
    fun pick(date: LocalDate) {
        pickedDay = if (date == today) FOLLOW_TODAY else date.toEpochDay()
    }
    // ADR 58: the timeline shows the plan the guards read (calendar events over the stored plan); editing stays on the stored one.
    val app = LocalContext.current.applicationContext as CompanionApp
    val plan = app.effectivePlan(state)

    val minute = now.truncatedTo(ChronoUnit.MINUTES)
    val timeline = remember(plan, selected, minute, state.events, focusedToday, model.today) {
        // The PC's events, and any only in this phone's own calendar (the same event in both shows once: the PC's copy, which the PC can answer for).
        val pcLooks = state.events.map { Triple(it.title.trim().lowercase(Locale.ROOT), it.localStart, it.localEnd) }.toSet()
        val ownOnly = app.calendarOverlay.ownEvents.filter { Triple(it.title.trim().lowercase(Locale.ROOT), it.localStart, it.localEnd) !in pcLooks }
        PlanTimelineBuilder.build(
            plan = plan, date = selected, now = now, events = state.events + ownOnly,
            focused = if (selected == today) focusedToday else Duration.ZERO,
            goal = Duration.ofMinutes((model.today?.goalMinutes ?: 0).toLong()),
            nightFallback = state.sleep::fallbackNightBefore, clock = clock,
        )
    }

    val weekStart = selected.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY))
    val cells = (0L until 7L).map { offset ->
        val date = weekStart.plusDays(offset)
        PlanDayCell(
            date = date, weekday = date.dayOfWeek.getDisplayName(TextStyle.SHORT, Locale.US), number = date.dayOfMonth.toString(),
            type = PlanDayNames.short(state.document.dayPlan, date), isToday = date == today, isSelected = date == selected,
        )
    }

    val weeks = plan.cycle.weeks.size
    val holding = planWait?.takeIf { it.appliesAt.isAfter(now.atZone(ZoneId.systemDefault()).toInstant()) }
    val paneModel = PlanPaneModel(
        title = "${selected.dayOfWeek.getDisplayName(TextStyle.FULL, Locale.US)} · ${PlanDayNames.full(state.document.dayPlan, selected)}",
        cells = cells,
        timeline = timeline,
        modeLine = modeLine(state, model, selected, today, timeline.hasWork),
        readOnlyNote = when {
            !canChange -> "Read-only: this phone can't change the plan because no PC is linked to take the edit. Link one in Settings › Devices & sync."
            selected.isBefore(today) -> "This day is over, so it can't be changed."
            else -> null
        },
        // The plan rests during a focus session, as on the PC and like the guard settings (the phone's own test session too).
        lockNote = guardsLock?.planSentence(),
        waitNote = holding?.let { "${it.pcName} is holding your change and applies it at about ${CLOCK.format(it.appliesAt.atZone(ZoneId.systemDefault()))}." },
        pending = state.pendingPlan,
        weekLabel = if (weeks > 1) "Week ${plan.cycle.weekNumberFor(selected)} of $weeks" else null,
        weekCount = weeks,
        weekNumber = plan.cycle.weekNumberFor(today),
        isToday = selected == today,
        canChange = canChange,
        locked = guardsLock != null,
    )

    com.accountabilibuddy.companion.ui.plan.EventActionsHost { onEvent ->
    PlanPane(
        model = paneModel,
        actions = PlanPaneActions(
            onPick = { cell -> pick(cell.date) },
            onShiftWeek = { by -> pick(selected.plusWeeks(by.toLong())) },
            onToday = { pickedDay = FOLLOW_TODAY },
            onChangeDay = onChangeDay,
            onTemplates = onTemplates,
            onCategories = onCategories,
            onSetWeek = onSetWeek,
            onDiscardEdit = onDiscardPlanEdit,
            onEvent = onEvent,
        ),
    )
    }
}

private const val FOLLOW_TODAY = Long.MIN_VALUE

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
