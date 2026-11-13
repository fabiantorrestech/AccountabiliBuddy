package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.ChevronRight
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.PhoneGoalStatus
import com.accountabilibuddy.companion.goals.PhoneGoalsOverview
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.ui.TodayActions
import com.accountabilibuddy.companion.ui.goals.GoalIcons
import com.accountabilibuddy.companion.ui.goals.GoalValueDialog
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalTodayCard
import com.accountabilibuddy.shared.goals.GoalTodayControl
import com.accountabilibuddy.shared.goals.GoalTodayRow
import com.accountabilibuddy.shared.goals.GoalTodayRows
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.util.UUID

/**
 * Today's goals for the phone's Today ([GoalTodayRows]): the goals with Show on Today on, with the same numbers the Goals tab shows
 * ([PhoneGoalsOverview], worked out off the main thread, again each minute and whenever the goals, the received files or the steps
 * change). Null while the goals aren't ready or can't be read: the part then draws nothing. Nothing here feeds a guard.
 */
@Composable
fun rememberTodayGoals(allReceived: List<ReceivedDevice>, received: List<ReceivedDevice>, inSession: Boolean): State<GoalTodayCard?> {
    val app = LocalContext.current.applicationContext as CompanionApp
    val status by app.goals.status.collectAsStateWithLifecycle()
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val display by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val steps by app.steps.state.collectAsStateWithLifecycle()
    val weekly by app.goals.deepWork.state.collectAsStateWithLifecycle()
    val daily by app.goals.deepWorkDaily.state.collectAsStateWithLifecycle()
    val minute by produceState(Instant.now()) {
        while (true) {
            val now = Instant.now()
            value = now
            delay(60_000L - (now.toEpochMilli() % 60_000L))
        }
    }
    return produceState<GoalTodayCard?>(null, status, lists, weekly, daily, display.weekStart, allReceived, received, steps, minute, inSession) {
        if (status != PhoneGoalStatus.Ready || lists.liveGoals.none { it.showOnToday }) {
            value = null
            return@produceState
        }
        value = withContext(Dispatchers.Default) {
            runCatching {
                val overview = PhoneGoalsOverview.build(lists, display, allReceived.map { it.file }, received.map { it.file }, steps, Instant.now(), ZoneId.systemDefault(), app.goals.deepWork.current(), com.accountabilibuddy.companion.goals.PhoneGoalData.slept(app))
                val names = overview.areas.associate { it.id to it.name }
                GoalTodayRows.of(overview.facts, names::get, inSession)
            }.getOrNull()
        }
    }
}

/**
 * "Today's goals" on the phone's Today (board Main): the card's head ("Today's goals 3 left", "Goals ›") and one row per goal with
 * Show on Today on. A habit has its tick, a value its Log button, time and steps a small bar.
 */
@Composable
fun TodayGoalsCard(card: GoalTodayCard, actions: TodayActions) {
    val colors = Ab.colors
    AbCard(contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 4.dp), verticalSpacing = 0.dp) {
        Row(Modifier.fillMaxWidth().heightIn(min = 32.dp).padding(bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Text("Today's goals", style = Ab.type.bodyMedium.copy(fontSize = 15.5.sp), fontWeight = FontWeight.Medium, color = colors.muted)
            Text(card.leftText, style = Ab.type.bodyMedium.copy(fontSize = 15.5.sp, fontFeatureSettings = "tnum"), color = colors.faint, modifier = Modifier.padding(start = 6.dp).weight(1f))
            GoalsLink(actions.onOpenGoals)
        }
        TodayGoalRows(card, actions)
    }
}

/** "Goals ›" in the accent: the Goals tab. */
@Composable
fun GoalsLink(onOpenGoals: () -> Unit) {
    Row(
        Modifier.heightIn(min = 32.dp).clickable(role = Role.Button, onClickLabel = "Open Goals", onClick = onOpenGoals),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        Text("Goals", style = Ab.type.small.copy(fontSize = 13.5.sp), fontWeight = FontWeight.SemiBold, color = Ab.colors.accent)
        Icon(Icons.Outlined.ChevronRight, contentDescription = null, tint = Ab.colors.accent, modifier = Modifier.size(18.dp))
    }
}

/**
 * The rows of Today's goals, for the card and for the half sheet of its More today chip (board B-Phone-Tray-Open: "tick or log
 * without leaving the session"). A tick or a logged value is a hand entry for today, as in the goal's own menu; a tap on the row
 * opens the goal's page.
 */
@Composable
fun TodayGoalRows(card: GoalTodayCard, actions: TodayActions, modifier: Modifier = Modifier) {
    val app = LocalContext.current.applicationContext as CompanionApp
    var logging by rememberSaveable { mutableStateOf<String?>(null) }
    Column(modifier.fillMaxWidth()) {
        card.rows.forEachIndexed { index, row ->
            if (index > 0) HorizontalDivider(color = Ab.colors.line, thickness = 1.dp)
            TodayGoalRow(
                row,
                onOpen = { actions.onOpenGoal(row.goalId) },
                onTick = { app.goals.addHandEntry(row.goalId, LocalDate.now(), if (row.checked) 0.0 else 1.0)?.let(actions.onMessage) },
                onLog = { logging = row.goalId.toString() },
            )
        }
    }
    logging?.let { raw ->
        val id = runCatching { UUID.fromString(raw) }.getOrNull()
        if (id == null) {
            logging = null
        } else {
            GoalValueDialog(
                "Log a value", "Value for today",
                onSave = { value -> app.goals.addHandEntry(id, LocalDate.now(), value).also { if (it != null) actions.onMessage(it) } == null },
                onDismiss = { logging = null },
            )
        }
    }
}

@Composable
private fun TodayGoalRow(row: GoalTodayRow, onOpen: () -> Unit, onTick: () -> Unit, onLog: () -> Unit) {
    val colors = Ab.colors
    val tint = GoalIcons.color(row.color) ?: colors.accent
    Row(
        Modifier.fillMaxWidth().heightIn(min = 52.dp).clickable(role = Role.Button, onClickLabel = "Open the goal", onClick = onOpen),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(Modifier.size(36.dp).clip(RoundedCornerShape(12.dp)).background(tint.copy(alpha = 0.18f).compositeOver(colors.surface2)), contentAlignment = Alignment.Center) {
            Icon(GoalIcons.of(row.icon), contentDescription = null, tint = tint, modifier = Modifier.size(20.dp))
        }
        Column(Modifier.weight(1f).padding(vertical = 6.dp)) {
            Text(row.name, style = Ab.type.body, fontWeight = FontWeight.Medium, color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(row.subLine, style = Ab.type.small, color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        when (row.control) {
            GoalTodayControl.Tick -> {
                row.number?.let { Text(it, style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted) }
                TickCircle(row.checked, row.name, onTick)
            }
            GoalTodayControl.Log -> Surface(onClick = onLog, shape = Ab.shapes.pill, color = colors.surface2, contentColor = colors.ink) {
                Box(Modifier.height(34.dp).padding(horizontal = 14.dp), contentAlignment = Alignment.Center) {
                    Text("Log", style = Ab.type.small.copy(fontSize = 13.5.sp), fontWeight = FontWeight.SemiBold)
                }
            }
            GoalTodayControl.Bar -> Box(Modifier.width(56.dp).height(6.dp).clip(CircleShape).background(colors.surface2)) {
                Box(Modifier.fillMaxWidth(row.fraction.coerceIn(0.0, 1.0).toFloat()).height(6.dp).background(colors.accent, CircleShape))
            }
            GoalTodayControl.None -> Unit
        }
    }
}

// The round tick of a habit: a 24 dp ring in a 44 dp touch target, filled in the "met" colour once today is done.
@Composable
private fun TickCircle(checked: Boolean, name: String, onTick: () -> Unit) {
    val colors = Ab.colors
    Box(
        Modifier.size(44.dp).clip(CircleShape).clickable(role = Role.Checkbox, onClick = onTick)
            .semantics { contentDescription = if (checked) "$name: done today, tap to undo" else "$name: mark done today" },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            Modifier.size(24.dp).clip(CircleShape).background(if (checked) colors.met else Color.Transparent)
                .border(2.dp, if (checked) colors.met else colors.faint, CircleShape),
            contentAlignment = Alignment.Center,
        ) {
            if (checked) Icon(Icons.Outlined.Check, contentDescription = null, tint = colors.bg, modifier = Modifier.size(16.dp))
        }
    }
}
