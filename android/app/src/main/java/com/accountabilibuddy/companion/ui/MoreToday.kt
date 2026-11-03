package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Restaurant
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.notify.TimelineNotifier
import com.accountabilibuddy.companion.sync.BeeminderGoals
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Instant
import java.time.LocalDateTime

/** The quiet "More today ⌄" line that opens and closes the fold. */
@Composable
fun MoreTodayToggle(expanded: Boolean, onToggle: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(role = Role.Button, onClick = onToggle),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp, Alignment.CenterHorizontally),
    ) {
        Text(if (expanded) "Less" else "More today", style = Ab.type.bodyMedium, color = Ab.colors.muted)
        Icon(if (expanded) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(20.dp))
    }
}

/** Which lunch chip shows outside the fold, if any. */
enum class LunchChipKind { Start, Back }

/**
 * The chip shows only when it is time: the plan's flexible break is open and lunch was not taken (Start), or lunch is under way
 * (Back). Null otherwise; the whole card, with the countdown and +5 / −5, is in More today.
 */
fun lunchChipKind(lunch: TodayLunch): LunchChipKind? {
    val taken = lunch.taken
    if (taken != null) return if (Instant.now().isBefore(taken.end)) LunchChipKind.Back else null
    val flex = lunch.flex ?: return null
    return if (lunch.workday && flex.window.contains(LocalDateTime.now())) LunchChipKind.Start else null
}

@Composable
fun LunchChip(lunch: TodayLunch, clock: ClockFormat, kind: LunchChipKind) {
    if (kind == LunchChipKind.Back) {
        val until = lunch.taken?.let { clock.time(LocalDateTime.ofInstant(it.end, java.time.ZoneId.systemDefault())) }.orEmpty()
        AbChip("Lunch until $until · I'm back", selected = false, onClick = lunch.onBack, icon = Icons.Outlined.Restaurant)
    } else {
        AbChip("Start lunch", selected = false, onClick = lunch.onStart, icon = Icons.Outlined.Restaurant)
    }
}

/** What the fold holds. Each piece shows only when it has something to say, in the order of the brief. */
class MoreTodayContent(
    val state: CompanionState,
    val model: DayBarModel,
    val clock: ClockFormat,
    val lunch: TodayLunch,
    val beeminder: BeeminderGoals?,
    val onImport: () -> Unit,
    val inSession: Boolean,
    val showWorkUse: Boolean,
    val showDayOff: Boolean,
)

/**
 * The fold's items: the day bar with its legend and sleep line, work use (while a PC session runs), lunch, Beeminder, the full
 * timers and alarms, the day-off note, the import empty state and the test session.
 */
fun LazyListScope.moreTodayItems(content: MoreTodayContent) {
    val model = content.model
    item(key = "more-daybar") { DayBarCard(model) }
    // Each piece is an item only when it has something to draw, so no empty item leaves a double gap.
    if (content.showWorkUse) item(key = "more-workuse") { WorkUseCard() }
    val lunch = content.lunch
    if (lunch.workday || lunch.flex != null || lunch.taken != null) item(key = "more-lunch") { LunchCard(lunch, content.clock) }
    content.beeminder?.let { goals -> item(key = "more-beeminder") { BeeminderCard(goals, content.clock) } }
    item(key = "more-timers") {
        val app = androidx.compose.ui.platform.LocalContext.current.applicationContext as CompanionApp
        TimersCard(app.timers, content.clock)
    }
    if (content.showDayOff) item(key = "more-dayoff") { DayOffCard() }
    if (!content.state.hasImport) {
        item(key = "more-import") { ImportCard(content.onImport) }
    }
    // The test session cannot start while a real one runs, so it is not offered then.
    if (!content.inSession) item(key = "more-test") { TestSessionCard() }
}

@Composable
private fun DayBarCard(model: DayBarModel) {
    val dark = Ab.colors.isDark
    AbCard(verticalSpacing = 8.dp) {
        model.sleepLine?.let { line ->
            val colour = Color(TimelineNotifier.sleepColor(line.amount, dark))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(8.dp).background(colour, CircleShape))
                Spacer(Modifier.width(6.dp))
                Text(line.text, style = Ab.type.bodyMedium, color = colour)
            }
        }
        DayBar(model)
        FocusLegend(model.focusLegend)
        if (model.summaryText.isNotEmpty()) Text(model.summaryText, style = Ab.type.small, color = Ab.colors.muted)
    }
}

/** The empty state for a phone that has no PC profiles yet. */
@Composable
private fun ImportCard(onImport: () -> Unit) {
    AbCard {
        IconTitle("Bring your PC's profiles", style = Ab.type.section)
        Text("Sign in to Google Drive in Settings › Devices & sync, or import a backup.", style = Ab.type.bodyMedium, color = Ab.colors.ink)
        AbTonalButton("Import backup file", onClick = onImport, modifier = Modifier.fillMaxWidth())
        Hint("On the PC: Settings › App & data › Backup › Export (without secrets, or with a password). Put the .abbackup file on the phone, then import it here.", label = "How to export")
    }
}
