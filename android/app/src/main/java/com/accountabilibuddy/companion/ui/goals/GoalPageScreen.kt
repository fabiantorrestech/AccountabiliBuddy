package com.accountabilibuddy.companion.ui.goals

import com.accountabilibuddy.companion.ui.kit.AbPageHeaderDefaults
import com.accountabilibuddy.companion.ui.kit.AbOutlinedButton
import androidx.compose.material.icons.automirrored.outlined.Notes
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Add
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material.icons.outlined.NotificationsNone
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material.icons.automirrored.outlined.ShowChart
import androidx.compose.material.icons.outlined.Checklist
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.PhoneGoalPage
import com.accountabilibuddy.companion.goals.PhoneGoalStatus
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalDayPanels
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalReadings
import com.accountabilibuddy.shared.goals.GoalTexts
import com.accountabilibuddy.shared.goals.GoalUnit
import java.time.LocalDate
import java.time.ZoneId
import java.util.UUID

/**
 * A goal's page (round 2 R6b1; brief §4.3 boards Goals-Phone-Habit and -Weight): back, the icon chip in the goal's colour, the name
 * with "15 min a day · Learning · 6 months going", ⋮ (the goal menu), then by kind:
 * - habit, time, milestone and other amounts: the tiles (Today, This period, Target with "What it takes ›", Streak / Total when on), the
 *   3-month heatmap with tap-to-select and the day panel under it (Tick it / Untick, a value by hand), then Progress tracking and
 *   Reminders rows (a milestone also shows "Open its tasks", Coming soon);
 * - a level to reach (weight): the latest value, the trend with its dashed projection and banner, Log weight, Target, the readings list.
 * Slots keep their height while the numbers load, so nothing jumps (brief §3.1). Weight is hand entries only (ADR 81 is Proposed).
 */
@Composable
fun GoalPageScreen(
    goalId: UUID,
    allReceived: List<ReceivedDevice>,
    received: List<ReceivedDevice>,
    onBack: () -> Unit,
    onMessage: (String) -> Unit,
    /** What the goal menu's "Start a session…" starts with; null greys the row. */
    session: GoalSessionStart? = null,
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val status by app.goals.status.collectAsStateWithLifecycle()
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val display by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val page = rememberGoalPage(goalId, allReceived, received).value
    var menuOpen by rememberSaveable { mutableStateOf(false) }
    var takesOpen by rememberSaveable { mutableStateOf(false) }
    var trackingOpen by rememberSaveable { mutableStateOf(false) }
    var logOpen by rememberSaveable { mutableStateOf(false) }
    // Edit goal: step 1 from the menu; "Change the target" and the weight page's Target open step 2 (R6b2).
    var editOpen by rememberSaveable { mutableStateOf(false) }
    var editAtTarget by rememberSaveable { mutableStateOf(false) }
    var editAtReminders by rememberSaveable { mutableStateOf(false) }
    val weekly by app.goals.deepWork.state.collectAsStateWithLifecycle()
    var editDay by rememberSaveable { mutableStateOf<Long?>(null) }
    var selectedDay by rememberSaveable(goalId) { mutableStateOf<Long?>(null) }

    // A goal that is gone (deleted here or elsewhere) goes back to the list; the goals not being ready say so.
    val stored = lists.liveGoals.firstOrNull { it.id == goalId }
    LaunchedEffect(status, stored == null) { if (status == PhoneGoalStatus.Ready && stored == null) onBack() }

    val tint = GoalIcons.color((page?.goal ?: stored)?.color) ?: Ab.colors.accent
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Header(page, stored?.name.orEmpty(), (page?.goal ?: stored)?.icon, tint, onBack = onBack, onMenu = { menuOpen = true })
        if (page == null) {
            // The numbers are being worked out: the tiles' slot holds its place.
            Box(Modifier.fillMaxWidth().height(260.dp))
            return@Column
        }

        val goal = page.goal
        val today = page.facts.today
        val zone = ZoneId.systemDefault()
        fun saveHand(day: LocalDate, value: Double): Boolean = app.goals.addHandEntry(goalId, day, value).also { if (it != null) onMessage(it) } == null

        if (page.usesTrend) {
            Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                GoalTrendCard(page, tint)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    val isWeight = goal.target.unit == GoalUnit.Lb || goal.target.unit == GoalUnit.Kg
                    AbPrimaryButton(if (isWeight) "Log weight" else "Log a value", onClick = { logOpen = true }, icon = Icons.Outlined.Add, modifier = Modifier.weight(1f))
                    AbOutlinedButton("Target", onClick = { editAtTarget = true; editOpen = true }, icon = Icons.Outlined.Edit)
                }
                GoalReadingsCard(GoalReadings.of(goal, page.data.entries, zone), goal.target.unit, today, onEditDay = { editDay = it.toEpochDay() })
                // The board's footnote, worded for the rule the numbers follow (your own edit wins over a reading, Q497).
                Text(
                    "The chart uses each day's first reading, or your own edit of that day. Tap a reading to change its day.",
                    style = Ab.type.small, color = Ab.colors.faint, modifier = Modifier.padding(horizontal = 4.dp),
                )
                NotesCard(stored?.notes, onEdit = { editAtTarget = true; editOpen = true })
                BottomRows(page, onTracking = { trackingOpen = true }, onReminders = { editAtTarget = false; editAtReminders = true; editOpen = true })
            }
        } else {
            Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                GoalTilesRow(page.tiles, tint, onWhatItTakes = if (page.takes != null) ({ takesOpen = true }) else null)
                if (goal.isDeepWork) weekly.pending?.let { DeepWorkPending(it, onCancel = { app.goals.deepWork.cancel() }) }
                val shownDay = selectedDay?.let { LocalDate.ofEpochDay(it) }?.takeIf { it < today }
                AbCard(verticalSpacing = 12.dp) {
                    GoalHeatmapCard(page, display.weekStart, display.heatmapLabels, tint, shownDay, onSelect = { day -> selectedDay = day.toEpochDay().takeIf { day < today } })
                    val day = shownDay ?: today
                    val panel = remember(page, day) { GoalDayPanels.of(goal, GoalCounting.day(goal, page.data, day, today), today, page.labelName) }
                    GoalDayPanelCard(
                        panel,
                        onBackToToday = if (shownDay != null) ({ selectedDay = null }) else null,
                        onTick = { value -> saveHand(day, value) },
                        onSave = { value -> saveHand(day, value) },
                    )
                }
                NotesCard(stored?.notes, onEdit = { editAtTarget = true; editOpen = true })
                BottomRows(page, onTracking = { trackingOpen = true }, onReminders = { editAtTarget = false; editAtReminders = true; editOpen = true })
            }
        }
    }

    if (menuOpen) {
        GoalMenuSheet(
            goalId, allReceived, received, onMessage, onDismiss = { menuOpen = false }, onDeleted = onBack,
            onEdit = { menuOpen = false; editAtTarget = false; editAtReminders = false; editOpen = true }, start = session,
            onReminders = { menuOpen = false; editAtTarget = false; editAtReminders = true; editOpen = true },
        )
    }
    if (editOpen) {
        GoalEditorSheets(goalId, allReceived, received, onMessage, onDismiss = { editOpen = false; editAtReminders = false }, startAtTarget = editAtTarget, startAtReminders = editAtReminders)
    }
    if (takesOpen && page?.takes != null) {
        val takes = page.takes
        AbSheet(title = takes.head, subtitle = takes.remaining, onDismiss = { takesOpen = false }, height = AbSheetHeight.Half) {
            takes.rows.forEach { row -> AbSheetRow(row.label, onClick = null, trailing = { AbSheetValue(row.value, opens = false) }) }
            takes.pace?.let { AbSheetRow(it, onClick = null) }
            AbSheetRow(takes.verdict, onClick = null, subtitle = null)
            AbSheetRow("Change the target", onClick = { takesOpen = false; editAtTarget = true; editOpen = true })
        }
    }
    if (trackingOpen && page != null) {
        AbSheet(title = "Progress tracking", subtitle = goal0(page), onDismiss = { trackingOpen = false }, height = AbSheetHeight.Half) {
            page.sourceLines.forEach { AbSheetRow(it.text, onClick = null, enabled = !it.comingSoon) }
        }
    }
    if (logOpen && page != null) {
        GoalValueDialog("Log a value", "Value for today", onSave = { v -> app.goals.addHandEntry(goalId, page.facts.today, v).also { if (it != null) onMessage(it) } == null }, onDismiss = { logOpen = false })
    }
    editDay?.let { epoch ->
        val day = LocalDate.ofEpochDay(epoch)
        GoalValueDialog("Edit this day's value", "Value for ${GoalTexts.date(day, LocalDate.now())}", onSave = { v -> app.goals.addHandEntry(goalId, day, v).also { if (it != null) onMessage(it) } == null }, onDismiss = { editDay = null })
    }
}

private fun goal0(page: PhoneGoalPage): String = page.goal.name

@Composable
private fun Header(page: PhoneGoalPage?, name: String, icon: String?, tint: androidx.compose.ui.graphics.Color, onBack: () -> Unit, onMenu: () -> Unit) {
    val colors = Ab.colors
    // The same space above as every tab's title (the shared page header), so opening a goal doesn't jump.
    Row(Modifier.fillMaxWidth().padding(top = AbPageHeaderDefaults.topSpace(), start = 4.dp, end = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Outlined.ArrowBack, contentDescription = "Back to Goals", tint = colors.accent) }
        Box(Modifier.size(44.dp).clip(RoundedCornerShape(12.dp)).background(tint.copy(alpha = 0.16f)), contentAlignment = Alignment.Center) {
            Icon(GoalIcons.of(icon), contentDescription = null, tint = tint, modifier = Modifier.size(24.dp))
        }
        Column(Modifier.weight(1f).padding(start = 12.dp)) {
            Text(name, style = Ab.type.head, color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(subtitle(page), style = Ab.type.small, color = colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
        IconButton(onClick = onMenu) { Icon(Icons.Outlined.MoreVert, contentDescription = "Goal menu", tint = colors.muted) }
    }
}

// "15 min a day" in ink · Learning · 6 months going; a level goal: "Fitness · amount over time · target 175 lb by Sun 3/1/2027".
@Composable
private fun subtitle(page: PhoneGoalPage?) = buildAnnotatedString {
    if (page == null) {
        append(" ")
        return@buildAnnotatedString
    }
    val goal = page.goal
    val facts = page.facts
    val target = GoalTexts.targetLine(goal, facts.today)
    val area = page.areaName
    val paused = facts.pausedSince?.let { " · paused since ${GoalTexts.date(it, facts.today)}" }.orEmpty()
    if (page.usesTrend) {
        append(listOfNotNull(area, "amount over time", "target $target").joinToString(" · ") + paused)
    } else {
        withStyle(SpanStyle(fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)) { append(target) }
        append(listOfNotNull(area, "${facts.age.short} going").joinToString(" · ", prefix = " · ") + paused)
    }
}

// The goal's notes, previewed (brief §4.4: "a Notes section with a preview on the goal's detail"): the first lines, a tap edits them
// in Edit goal. Private text: drawn here only. Nothing is drawn for a goal without notes.
@Composable
private fun NotesCard(notes: String?, onEdit: () -> Unit) {
    val text = notes?.trim().orEmpty()
    if (text.isEmpty()) return
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 8.dp), verticalSpacing = 0.dp) {
        AbListRow("Notes", icon = Icons.AutoMirrored.Outlined.Notes, subtitle = text.lineSequence().take(3).joinToString("\n").take(240), onClick = onEdit)
    }
}

@Composable
private fun BottomRows(page: PhoneGoalPage, onTracking: () -> Unit, onReminders: () -> Unit) {
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 8.dp), verticalSpacing = 0.dp) {
        val lines = page.sourceLines
        AbListRow(
            "Progress tracking", icon = Icons.AutoMirrored.Outlined.ShowChart, subtitle = lines.firstOrNull { !it.comingSoon }?.text ?: lines.firstOrNull()?.text,
            onClick = onTracking, divider = true,
        )
        // Board Goals-Phone-Habit: "Reminders", "Next: today 8:00 PM", "4 ›".
        AbListRow(
            "Reminders", icon = Icons.Outlined.NotificationsNone,
            subtitle = page.reminderNext ?: if (page.reminderCount == 0) "None yet · add one" else "None coming up",
            onClick = onReminders, divider = page.goal.kind == GoalKind.Milestone,
            trailing = if (page.reminderCount > 0) ({ Text("${page.reminderCount}", style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = Ab.colors.muted) }) else null,
        )
        if (page.goal.kind == GoalKind.Milestone) {
            AbListRow("Open its tasks", icon = Icons.Outlined.Checklist, subtitle = "Coming soon", showChevron = false)
        }
    }
}

// A waiting lowering of Deep work's weekly target (brief §5.7): what it lowers to and when, "never during a session", with Cancel.
// There is no "apply now": it applies by itself once its time has come and no session runs.
@Composable
private fun DeepWorkPending(pending: com.accountabilibuddy.shared.goals.GoalPendingChange, onCancel: () -> Unit) {
    val at = pending.appliesAt.atZoneSameInstant(ZoneId.systemDefault()).format(java.time.format.DateTimeFormatter.ofPattern("h:mm a", java.util.Locale.US))
    val to = pending.amount?.let { GoalTexts.minutes(it) } ?: "no target"
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 8.dp), verticalSpacing = 0.dp) {
        AbListRow(
            "Weekly target lowers to $to at $at", icon = Icons.Outlined.Schedule, subtitle = "Never during a session",
            showChevron = false, trailing = { AbTextButton("Cancel", onClick = onCancel) },
        )
    }
}
