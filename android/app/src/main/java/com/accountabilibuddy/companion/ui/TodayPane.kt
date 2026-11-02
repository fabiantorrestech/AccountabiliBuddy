package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.material3.Text
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.nudge.NudgeStore
import com.accountabilibuddy.companion.placement.PlacementSession
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Instant
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.sync.BeeminderGoals
import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.companion.usage.PhoneUsage
import com.accountabilibuddy.shared.sync.SessionTaskItem
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * Today (boards 5a, 5b, 6a): the pane for the first tab, split into the header ([TodayHeader]), the hero ([IdleHero], [SessionHero],
 * [PlanningHero]), the cards ([RingCard], [ComebackCard], [StepsCard], the small cards, [GuardBanner]) and the fold ([moreTodayItems]).
 * No Scaffold and no fixed width: it fills the pane it is given (the phone's column now, the tablet's pinned 400 dp column later).
 * The buttons of a running session stay at the bottom of the pane; everything else scrolls. Only the timer redraws each second.
 */
@Composable
fun TodayPane(
    state: CompanionState,
    model: DayBarModel,
    clock: ClockFormat,
    pc: TodayPc,
    comebacks: OpenComebacks?,
    phoneUsage: PhoneUsage?,
    /** Set while a focus session runs on this phone's view of things; the banner says the settings rest. */
    guardsLock: GuardsLock?,
    guardOn: Boolean,
    lunch: TodayLunch,
    /** The user's Beeminder goals from a PC's sync file (ADR 43); null hides the card. */
    beeminder: BeeminderGoals?,
    /** The noise player, when a sound folder is set; null hides it. */
    noiseCard: (@Composable () -> Unit)?,
    actions: TodayActions,
    /** The sync light and its refresh, at the end of the header line. */
    syncLight: @Composable () -> Unit,
    /** Quiet cards above the hero (the "New since your last version" card); empty when there is nothing to say. */
    banner: @Composable () -> Unit = {},
) {
    // A phone with no plan yet starts with the fold open: the import card is in it.
    var expanded by rememberSaveable { mutableStateOf(!state.hasImport) }
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val showWorkUse = remember(model, pc) { PlacementSession.current(app, Instant.now())?.takeUnless { it.onBreak } != null }
    val showDayOff = remember(model) { NudgeStore(context).load().dayOff == LocalDate.now() }
    var starting by remember { mutableStateOf(false) }
    val session = pc.state?.takeIf { it.isActive }
    val stepAway = if (session != null) rememberStepAway(pc, clock) else null
    val hasTimer = rememberHasTimerOrAlarm()
    val chip = lunchChipKind(lunch)

    Column(Modifier.fillMaxSize()) {
        LazyColumn(
            Modifier.weight(1f),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 4.dp, bottom = 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (session == null) {
                idleItems(state.hasImport, model, clock, pc, comebacks, phoneUsage, hasTimer, chip, lunch, guardsLock, guardOn, noiseCard, actions, syncLight, banner, onStart = { starting = true })
            } else {
                sessionItems(model, clock, pc, session, stepAway?.text, chip, lunch, guardsLock, guardOn, noiseCard, actions, syncLight, banner)
            }
            item(key = "more-toggle") { MoreTodayToggle(expanded, onToggle = { expanded = !expanded }) }
            if (expanded) moreTodayItems(MoreTodayContent(state, model, clock, lunch, beeminder, actions.onImport, inSession = session != null, showWorkUse = showWorkUse, showDayOff = showDayOff))
        }
        if (session != null) {
            SessionActions(
                stepAway, canEndEarly = pc.updateNeeded == null && pc.waiting == null, onEndEarly = actions.onEndEarly,
                modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 12.dp),
            )
        }
    }

    val pcState = pc.state
    if (starting && pcState != null && pc.name != null) {
        StartOnPcDialog(
            pcState, pc.name,
            onStart = { profileId, minutes, countUp ->
                starting = false
                actions.onStartOnPc(profileId, minutes, countUp)
            },
            onCancel = { starting = false },
        )
    }
}

private val DATE_FORMAT: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE, MMM d", Locale.getDefault())

// Nothing running (board 5a): date line, the two-line headline and the way to start, the cards, then the fold.
private fun androidx.compose.foundation.lazy.LazyListScope.idleItems(
    hasPlan: Boolean,
    model: DayBarModel,
    clock: ClockFormat,
    pc: TodayPc,
    comebacks: OpenComebacks?,
    phoneUsage: PhoneUsage?,
    hasTimer: Boolean,
    chip: LunchChipKind?,
    lunch: TodayLunch,
    guardsLock: GuardsLock?,
    guardOn: Boolean,
    noiseCard: (@Composable () -> Unit)?,
    actions: TodayActions,
    syncLight: @Composable () -> Unit,
    banner: @Composable () -> Unit,
    onStart: () -> Unit,
) {
    // A phone with no plan from the PC yet cannot say what is next (its default plan would read as a day off).
    val info = model.today?.let { if (hasPlan) it else it.copy(dateSuffix = null, next = "No plan on this phone yet.") }
    item(key = "header") {
        Column {
            TodayHeader(listOfNotNull(LocalDate.now().format(DATE_FORMAT), info?.dateSuffix).joinToString(" · "), syncLight)
            banner()
        }
    }
    item(key = "hero") {
        Column(Modifier.fillMaxWidth()) {
            IdleHero(info, model.statusText, pc, clock, locked = guardsLock != null, onStart, actions.onCancelWaiting)
        }
    }
    if (guardsLock != null) item(key = "guard") { GuardBanner(guardsLock, guardOn) }
    if (chip != null) item(key = "lunch-chip") { LunchChip(lunch, clock, chip) }
    if (!pc.hasPc) item(key = "pc-missing") { PcMissingCard(actions.onOpenDevices) }
    if (info != null && (info.goalMinutes != null || info.focusedMinutes > 0)) item(key = "ring") { RingCard(info.focusedMinutes, info.goalMinutes) }
    if (comebacks != null && comebacks.items.isNotEmpty()) item(key = "comebacks") { ComebackCard(comebacks, actions.onOpenTasks) }
    item(key = "small") { SmallCardsRow(hasTimer, phoneUsage, clock, actions.onAllowUsage) }
    noiseCard?.let { card -> item(key = "noise") { card() } }
}

// A running session (boards 5b, 6a), planning stage, or break: the timer first, then the steps, the ring and the guard banner.
private fun androidx.compose.foundation.lazy.LazyListScope.sessionItems(
    model: DayBarModel,
    clock: ClockFormat,
    pc: TodayPc,
    session: com.accountabilibuddy.companion.sync.PcState,
    awayText: String?,
    chip: LunchChipKind?,
    lunch: TodayLunch,
    guardsLock: GuardsLock?,
    guardOn: Boolean,
    noiseCard: (@Composable () -> Unit)?,
    actions: TodayActions,
    syncLight: @Composable () -> Unit,
    banner: @Composable () -> Unit,
) {
    val info = model.today
    val tasks = pc.tasks
    val shown = tasks?.let { SessionTaskItem.shown(it.tasks, it.currentId)?.first }
    val planning = session.isPlanning
    val place = tasks?.takeIf { it.tasks.isNotEmpty() }?.let { list ->
        val index = shown?.let { list.tasks.indexOf(it) + 1 } ?: list.tasks.size
        " · $index of ${list.tasks.size}"
    }.orEmpty()
    val headerText = when {
        planning -> "Plan first on ${pc.name}"
        session.isBreak -> "Break on ${pc.name}"
        else -> "Focus on ${pc.name}$place"
    }
    item(key = "header") {
        Column {
            TodayHeader(headerText, syncLight, icon = Icons.Outlined.Computer)
            banner()
        }
    }
    item(key = "hero") {
        val planEnd = session.planningEndsAt?.toInstant()
        if (planning && planEnd != null) {
            PlanningHero(session.planningStartedAt?.toInstant(), planEnd, session.profileName)
        } else {
            val title = when {
                session.isBreak -> "Break"
                shown != null -> shown.title
                tasks != null && tasks.tasks.isNotEmpty() -> "All tasks done"
                else -> null
            }
            SessionHero(session, title, clock, awayText, pc.awaySince)
        }
    }
    if (pc.updateNeeded != null) item(key = "update-needed") { MutedLine(pc.updateNeeded) }
    if (pc.waiting != null) item(key = "waiting") { Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { WaitingLine(pc, clock, actions.onCancelWaiting) } }
    if (!planning && !session.isBreak && tasks != null && tasks.tasks.isNotEmpty()) item(key = "steps") { StepsCard(tasks) }
    if (info != null && (info.goalMinutes != null || info.focusedMinutes > 0)) item(key = "ring") { RingCard(info.focusedMinutes, info.goalMinutes) }
    // The Budget pace the PC's own card showed (ADR 47); amber-ish (the waiting colour) when the day can't hold the rest.
    info?.pace?.let { pace -> item(key = "pace") { Text(BudgetPaceLine.text(pace), style = Ab.type.bodyMedium, color = if (pace.behind) Ab.meaning(Ab.colors.waiting) else Ab.colors.muted) } }
    // Every task ticked: the phone's way out is still the PC's wait; the PC can end it at once.
    if (!planning && tasks != null && tasks.tasks.isNotEmpty() && tasks.tasks.all { it.isDone }) {
        item(key = "all-done") { MutedLine("All tasks are ticked. On the PC you can end the session at once.") }
    }
    if (chip != null) item(key = "lunch-chip") { LunchChip(lunch, clock, chip) }
    if (guardsLock != null) item(key = "guard") { GuardBanner(guardsLock, guardOn) }
    noiseCard?.let { card -> item(key = "noise") { card() } }
}
