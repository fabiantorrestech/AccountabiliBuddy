package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.HorizontalDivider
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.engine.TodayInfo
import com.accountabilibuddy.companion.guidance.isCompanionMode
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.sync.PcState
import com.accountabilibuddy.companion.today.TodayItem
import com.accountabilibuddy.companion.today.TodayLayout
import com.accountabilibuddy.companion.today.TodayPlacement
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.companion.ui.today.CurrentItem
import com.accountabilibuddy.companion.ui.today.MoreTodayChip
import com.accountabilibuddy.companion.ui.today.MoreTodayChipRow
import com.accountabilibuddy.companion.ui.today.NoisePickerSheet
import com.accountabilibuddy.companion.ui.today.SessionActionBar
import com.accountabilibuddy.companion.ui.today.SessionCard
import com.accountabilibuddy.companion.ui.today.SessionMenuSheet
import com.accountabilibuddy.companion.ui.today.StartActionBar
import com.accountabilibuddy.companion.ui.today.ThisSessionCard
import com.accountabilibuddy.companion.ui.today.TodayNoiseBar
import com.accountabilibuddy.companion.ui.today.TodayOptionsSheet
import com.accountabilibuddy.companion.ui.today.rememberTaskTicker
import com.accountabilibuddy.shared.WatchText
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * The phone's Today (round 2, brief §4.1; boards Main, B-Phone-Session, B-Phone-Tray-Open): the header (Today, the date and PB, one
 * ⋮), the session card or what's next, This session, the parts the layout shows ([todayItem]), the More today chips (each opens a
 * half sheet), and the thumb zone above the tabs: the action bar (Done · step, Step away, ⋯; or Start) and the noise bar.
 *
 * Same skeleton in and out of a session (CN §1 rule 7): the card slot and the list slot stay where they are, and the thumb zone is
 * always at the bottom. Nothing here decides anything: Step away, End session and Start are the paths Today always used, and the
 * safety parts ([TodayItem.allowed]) are drawn wherever the layout may put them, never hidden.
 */
@Composable
internal fun PhoneToday(
    state: CompanionState,
    model: DayBarModel,
    clock: ClockFormat,
    pc: TodayPc,
    info: TodayInfo?,
    parts: TodayItemContext,
    layout: TodayLayout,
    guardsLock: GuardsLock?,
    stepAway: StepAwayState?,
    actions: TodayActions,
    syncLight: @Composable () -> Unit,
    banner: @Composable () -> Unit,
    onStart: () -> Unit,
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val noiseState by app.noise.state.collectAsState()
    val noiseSettings by app.noise.settings.collectAsState()
    val companion = isCompanionMode()
    val session = pc.state?.takeIf { it.isActive }
    val locked = guardsLock != null && !guardsLock.test

    var openPart by remember { mutableStateOf<TodayItem?>(null) }
    var sessionMenu by remember { mutableStateOf(false) }
    var noisePicker by remember { mutableStateOf(false) }
    var options by remember { mutableStateOf(false) }
    var arranging by remember { mutableStateOf(false) }

    // A phone with no plan yet shows the import card on Today itself (the fold used to start open for it).
    fun placeOf(part: TodayItem): TodayPlacement {
        val placed = layout.placementOf(part)
        return if (part == TodayItem.Import && !state.hasImport && placed == TodayPlacement.Fold) TodayPlacement.Above else placed
    }
    val ordered = layout.orderFor(parts.inSession)
    val above = ordered.filter { placeOf(it) == TodayPlacement.Above && it != TodayItem.Noise }
    val chips = ordered.filter { placeOf(it) == TodayPlacement.Fold }.mapNotNull { part ->
        when {
            part == TodayItem.Noise -> if (noiseState.hasFolder) MoreTodayChip(part.key, "Noise", part.icon()) { noisePicker = true } else null
            TodayBlocks().apply { todayItem(part, parts) }.blocks.isEmpty() -> null
            else -> MoreTodayChip(part.key, part.title, part.icon()) { openPart = part }
        }
    }
    val showNoiseBar = noiseState.hasFolder && layout.placementOf(TodayItem.Noise) == TodayPlacement.Above

    val running = pc.tasks
    val ticker = running?.let { rememberTaskTicker(it) }
    val current = if (session != null && !session.isPlanning && !session.isBreak) CurrentItem.of(running) else null
    val currentId = current?.item?.id
    val canDone = ticker != null && ticker.canTick && currentId != null && !ticker.sending(currentId)

    Column(Modifier.fillMaxSize()) {
        LazyColumn(
            Modifier.weight(1f),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 4.dp, bottom = 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item(key = "header") {
                Column {
                    TodayTitleLine(info, pc, session, companion, syncLight, onOptions = { options = true })
                    banner()
                }
            }
            if (session == null) {
                item(key = "hero") {
                    Column(Modifier.fillMaxWidth()) {
                        IdleHero(info, model.statusText, pc, clock, locked, onStart, actions.onCancelWaiting, compact = true, showStart = false)
                    }
                }
            } else {
                sessionItems(session, pc, clock, stepAway, actions)
            }
            val sink = asTodaySink()
            above.forEach { part -> sink.todayItem(part, parts) }
            if (chips.isNotEmpty()) item(key = "more-today") { MoreTodayChipRow(chips, Modifier.padding(top = 8.dp)) }
        }

        // The thumb zone (CN §3): always at the bottom of the pane, above the tabs and the park pill.
        Column(Modifier.fillMaxWidth().background(Ab.colors.bg)) {
            if (session != null) {
                SessionActionBar(
                    current = current, canDone = canDone, onDone = { ticker?.tick(currentId) }, stepAway = stepAway,
                    onMore = { sessionMenu = true },
                    modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 10.dp),
                )
            } else if (canStartHere(pc, locked)) {
                StartActionBar(pc.startName, onStart, Modifier.padding(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 10.dp))
            }
            if (showNoiseBar) {
                HorizontalDivider(color = Ab.colors.line, thickness = 1.dp)
                TodayNoiseBar(noiseState, noiseSettings, app.noise, onOpenPicker = { noisePicker = true })
            }
        }
    }

    openPart?.let { part ->
        AbSheet(title = part.title, subtitle = part.about, onDismiss = { openPart = null }, height = AbSheetHeight.Half) {
            Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                TodayBlocks().apply { todayItem(part, parts) }.blocks.forEach { block -> Box { block.content() } }
            }
        }
    }
    if (sessionMenu && session != null) {
        SessionMenuSheet(
            state = session, pcName = pc.name, clock = clock, awaySince = pc.awaySince, stepAway = stepAway,
            endBlocked = pc.updateNeeded ?: pc.waiting?.let { "Waiting for ${pc.name ?: "the PC"}'s answer" },
            onEndEarly = actions.onEndEarly, onDismiss = { sessionMenu = false },
        )
    }
    if (noisePicker) {
        NoisePickerSheet(noiseState, noiseSettings, app.noise, onOpenNoiseSettings = actions.onOpenNoiseSettings, onDismiss = { noisePicker = false })
    }
    if (options) {
        TodayOptionsSheet(
            noiseTitle = if (noiseState.hasFolder) noiseState.title ?: "Choose a sound" else null,
            onArrange = { arranging = true }, onNoise = { noisePicker = true }, onDismiss = { options = false },
        )
    }
    if (arranging) TodayLayoutSheet(onDismiss = { arranging = false }, startInSession = session != null)
}

private val DATE: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE, MMM d", Locale.getDefault())

// The header with its date line: the date and what the day is, or, in a session, where it runs ("Focus on Work PC · 1 of 3").
@Composable
private fun TodayTitleLine(
    info: TodayInfo?,
    pc: TodayPc,
    session: PcState?,
    companion: Boolean,
    syncLight: @Composable () -> Unit,
    onOptions: () -> Unit,
) {
    val date = LocalDate.now().format(DATE)
    val line = if (session == null) {
        listOfNotNull(date, info?.dateSuffix).joinToString(" · ")
    } else {
        val tasks = pc.tasks?.tasks.orEmpty()
        val shown = pc.tasks?.let { com.accountabilibuddy.shared.sync.SessionTaskItem.shown(it.tasks, it.currentId)?.first }
        val place = if (tasks.isEmpty()) "" else " · ${shown?.let { tasks.indexOf(it) + 1 } ?: tasks.size} of ${tasks.size}"
        val where = when {
            session.isPlanning -> "Plan first on ${pc.name}"
            session.isBreak -> "Break on ${pc.name}"
            else -> "Focus on ${pc.name}$place"
        }
        "$date · $where"
    }
    com.accountabilibuddy.companion.ui.today.TodayTitleHeader(line, companion, syncLight, onOptions)
}

// A running session's head on the phone: the session card (or the planning count), what the PC says, and This session.
private fun androidx.compose.foundation.lazy.LazyListScope.sessionItems(
    session: PcState,
    pc: TodayPc,
    clock: ClockFormat,
    stepAway: StepAwayState?,
    actions: TodayActions,
) {
    val tasks = pc.tasks
    val planning = session.isPlanning
    item(key = "hero") {
        val planEnd = session.planningEndsAt?.toInstant()
        if (planning && planEnd != null) {
            PlanningHero(session.planningStartedAt?.toInstant(), planEnd, session.profileName)
        } else {
            SessionCard(session, clock, stepAway?.text, pc.awaySince)
        }
    }
    if (pc.updateNeeded != null) item(key = "update-needed") { MutedLine(pc.updateNeeded) }
    if (pc.offMinutesToday > 0) {
        item(key = "pc-off-today") { MutedLine("PC off ${WatchText.duration(pc.offMinutesToday)} during sessions today (not counted)") }
    }
    if (pc.waiting != null) item(key = "waiting") { Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { WaitingLine(pc, clock, actions.onCancelWaiting) } }
    if (!planning && !session.isBreak && tasks != null && tasks.tasks.isNotEmpty()) item(key = "this-session") { ThisSessionCard(tasks) }
}
