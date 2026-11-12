package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.widthIn
import androidx.compose.ui.Alignment
import com.accountabilibuddy.companion.today.TodayItem
import com.accountabilibuddy.companion.ui.shell.LocalExpanded
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
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
import com.accountabilibuddy.companion.today.TodayPlacement
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
 * [PlanningHero]), the cards ([RingCard], [ComebackCard], [StepsCard], the small cards, [GuardBanner]) and the fold. Which cards show, where
 * and in what order is the person's layout ([todayItem], [com.accountabilibuddy.companion.today.TodayLayout]); with none chosen it is the order Today always had.
 * No Scaffold and no fixed width: it fills the pane it is given (the phone's column now, the tablet's pinned 400 dp column later).
 * The buttons of a running session stay at the bottom of the pane; everything else scrolls. Only the timer redraws each second.
 * On a phone (round 2, brief §4.1) the pane is [PhoneToday]: the same parts with the round 2 header, session card, This session,
 * More today chips and the thumb zone; the tablet keeps the dashboard below until its own round 2 step.
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
    /** Every received device, removed devices' kept copies included, and the ones still in the group: Today's goals count from them, as the Goals tab does. */
    allReceived: List<com.accountabilibuddy.companion.sync.ReceivedDevice> = emptyList(),
    received: List<com.accountabilibuddy.companion.sync.ReceivedDevice> = emptyList(),
) {
    // A phone with no plan yet starts with the fold open: the import card is in it.
    var expanded by rememberSaveable { mutableStateOf(!state.hasImport) }
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val stepsState by app.steps.state.collectAsState()
    val showWorkUse = remember(model, pc) { PlacementSession.current(app, Instant.now())?.takeUnless { it.onBreak } != null }
    val showDayOff = remember(model) { NudgeStore(context).load().dayOff == LocalDate.now() }
    var starting by remember { mutableStateOf(false) }
    val session = pc.state?.takeIf { it.isActive }
    val stepAway = if (session != null) rememberStepAway(pc, clock) else null
    val hasTimer = rememberHasTimerOrAlarm()
    val chip = lunchChipKind(lunch)
    val idleLayout by UiPrefs.todayLayout.collectAsState()
    val ownSessionLayout by UiPrefs.todaySessionLayout.collectAsState()
    // Round 2: a session has its own layout once the person sets one (Settings › Today); until then the one layout serves both.
    // Parts with a session default of their own (Today's goals, Next up, Deep work, Daily steps) take it there either way.
    val layout = if (session != null) ownSessionLayout ?: idleLayout.copy(session = true) else idleLayout
    // A phone with no plan from the PC yet cannot say what is next (its default plan would read as a day off).
    val info = model.today?.let { if (session == null && !state.hasImport) it.copy(dateSuffix = null, next = "No plan on this phone yet.") else it }

    // The tablet draws the same parts as a dashboard ([WideToday]); with it the timeline runs across the top, wherever the layout put it
    // (a part the person hid stays hidden).
    val wide = LocalExpanded.current
    val todayGoals by com.accountabilibuddy.companion.ui.today.rememberTodayGoals(allReceived, received, inSession = session != null)
    val dayBarOnTop = wide && layout.placementOf(TodayItem.DayBar) != TodayPlacement.Hidden
    val parts = TodayItemContext(
        inSession = session != null, state = state, model = model, info = info, clock = clock, pc = pc, comebacks = comebacks,
        phoneUsage = phoneUsage, hasTimer = hasTimer, lunchChip = chip, lunch = lunch, guardsLock = guardsLock, guardOn = guardOn,
        beeminder = beeminder, noiseCard = noiseCard, stepsState = stepsState, actions = actions,
        allTasksTicked = session != null && !session.isPlanning && pc.tasks != null && pc.tasks.tasks.isNotEmpty() && pc.tasks.tasks.all { it.isDone },
        showWorkUse = showWorkUse, showDayOff = showDayOff, goals = todayGoals,
    )
    val body: TodaySink.() -> Unit = {
        if (session == null) {
            idleHead(info, model, pc, clock, guardsLock, syncLight, banner, actions, onStart = { starting = true })
        } else {
            sessionHead(model, clock, pc, session, stepAway?.text, actions, syncLight, banner)
        }
        // The arrangeable parts (Settings › Appearance › Today layout): the person's order and places. With nothing chosen
        // this is how Today was always drawn. The head above, and the safety parts the layout can't hide, always show.
        val above = layout.itemsAt(TodayPlacement.Above, parts.inSession).filter { !(dayBarOnTop && it == TodayItem.DayBar) }
        above.forEach { part -> todayItem(part, parts) }
        val folded = layout.itemsAt(TodayPlacement.Fold, parts.inSession).filter { !(dayBarOnTop && it == TodayItem.DayBar) }
        if (folded.isNotEmpty()) item(key = "more-toggle") { MoreTodayToggle(expanded, onToggle = { expanded = !expanded }) }
        if (expanded) folded.forEach { part -> todayItem(part, parts) }
        if (dayBarOnTop) todayItem(TodayItem.DayBar, parts)
    }

    if (!wide) {
        // Round 2 (brief §4.1): the phone's own Today, with the session card, This session, More today chips and the thumb zone.
        PhoneToday(
            state = state, model = model, clock = clock, pc = pc, info = info, parts = parts, layout = layout, guardsLock = guardsLock,
            stepAway = stepAway, actions = actions, syncLight = syncLight, banner = banner, onStart = { starting = true },
        )
    } else {
        Column(Modifier.fillMaxSize()) {
            WideToday(TodayBlocks().apply(body).blocks, Modifier.weight(1f))
            if (session != null) {
                // Not full width: a centred row of the buttons, as wide as a phone.
                Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                    SessionActions(
                        stepAway, canEndEarly = pc.updateNeeded == null && pc.waiting == null, onEndEarly = actions.onEndEarly,
                        modifier = Modifier.widthIn(max = 560.dp).padding(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 12.dp),
                    )
                }
            }
        }
    }

    val pcState = pc.state
    if (starting && pcState != null && pc.hasPc) {
        StartOnPcDialog(
            pcState, pc.startName,
            onStart = { profileId, minutes, countUp ->
                starting = false
                actions.onStartOnPc(profileId, minutes, countUp)
            },
            onCancel = { starting = false },
        )
    }
}

private val DATE_FORMAT: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE, MMM d", Locale.getDefault())

// Nothing running (board 5a): date line, the two-line headline and the way to start. The cards follow as arranged ([todayItem]).
private fun TodaySink.idleHead(
    info: com.accountabilibuddy.companion.engine.TodayInfo?,
    model: DayBarModel,
    pc: TodayPc,
    clock: ClockFormat,
    guardsLock: GuardsLock?,
    syncLight: @Composable () -> Unit,
    banner: @Composable () -> Unit,
    actions: TodayActions,
    onStart: () -> Unit,
) {
    item(key = "header") {
        Column {
            TodayHeader(listOfNotNull(LocalDate.now().format(DATE_FORMAT), info?.dateSuffix).joinToString(" · "), syncLight)
            banner()
        }
    }
    item(key = "hero") {
        Column(Modifier.fillMaxWidth()) {
            IdleHero(info, model.statusText, pc, clock, locked = guardsLock != null && !guardsLock.test, onStart, actions.onCancelWaiting)
        }
    }
}

// A running session (boards 5b, 6a), planning stage, or break: the header, the timer, what the PC says and the task list. The cards follow as arranged.
private fun TodaySink.sessionHead(
    model: DayBarModel,
    clock: ClockFormat,
    pc: TodayPc,
    session: com.accountabilibuddy.companion.sync.PcState,
    awayText: String?,
    actions: TodayActions,
    syncLight: @Composable () -> Unit,
    banner: @Composable () -> Unit,
) {
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
    if (pc.offMinutesToday > 0) {
        item(key = "pc-off-today") { MutedLine("PC off ${com.accountabilibuddy.shared.WatchText.duration(pc.offMinutesToday)} during sessions today (not counted)") }
    }
    if (pc.waiting != null) item(key = "waiting") { Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { WaitingLine(pc, clock, actions.onCancelWaiting) } }
    if (!planning && !session.isBreak && tasks != null && tasks.tasks.isNotEmpty()) item(key = "steps") { StepsCard(tasks) }
}
