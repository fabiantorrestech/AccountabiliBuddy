package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Home
import androidx.compose.material.icons.outlined.Link
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Text
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.GoalHeatmapLabels
import com.accountabilibuddy.companion.goals.GoalSettingsPrefs
import com.accountabilibuddy.companion.ui.GuardsRestBanner
import com.accountabilibuddy.companion.ui.LocalGuardsLock
import com.accountabilibuddy.companion.ui.LocalGuardsLocked
import com.accountabilibuddy.companion.ui.goals.GoalAreaSheet
import com.accountabilibuddy.companion.ui.goals.GoalAreasSheet
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.kit.abTimePickerColors
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetCheck
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalWeekStart
import java.time.DayOfWeek
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

/**
 * Settings › Goals (round 2 R6b2; brief §4.7, the PC's Settings › Goals): the weekly review (day, time, snooze length, tidy-up straight
 * after), the default reminders (skip when done; nudge when behind, one default per mode, ADR 69), charts (week start, heatmap day
 * labels, the extra tiles default; also in Goals › Options), Areas, a link to Connections (Beeminder, Health Connect, calendars) and the
 * pausing rules in words. Everything is this phone's own and saved at once ([GoalSettingsPrefs], [GoalDisplayPrefs]): suggestions and
 * display only, nothing a guard reads. Like the other Settings pages it rests (view-only, "Settings rest until …") while a focus
 * session runs. The review and the reminders that use these choices arrive in the next update.
 */
@Composable
fun GoalsSettingsPage(anchor: String?, onOpenConnections: () -> Unit) {
    val lock = LocalGuardsLock.current
    val rows = buildList {
        if (lock != null) add(PageRow(REST_BANNER) { GuardsRestBanner(lock) })
        add(PageRow(SettingsAnchors.GOALS_REVIEW) { ReviewCard() })
        add(PageRow(SettingsAnchors.GOALS_REMINDERS) { ReminderDefaultsCard() })
        add(PageRow(SettingsAnchors.GOALS_CHARTS) { ChartsCard() })
        add(PageRow(SettingsAnchors.GOALS_AREAS) { AreasAndConnections(onOpenConnections) })
        add(PageRow(SettingsAnchors.GOALS_PAUSING) { PausingCard() })
    }
    SettingsPageList(rows, anchor)
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ReviewCard() {
    val context = LocalContext.current
    val review by GoalSettingsPrefs.review.collectAsStateWithLifecycle()
    val locked = LocalGuardsLocked.current
    var dayOpen by rememberSaveable { mutableStateOf(false) }
    var timeOpen by rememberSaveable { mutableStateOf(false) }
    val days = listOf<DayOfWeek?>(null) + DayOfWeek.entries.let { listOf(it[6]) + it.take(6) }

    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
        AbSectionLabel("Weekly review", Modifier.padding(start = 4.dp, bottom = 4.dp))
        AbListRow(
            "Day", icon = Icons.Outlined.CalendarMonth, subtitle = "The review looks back at the week before",
            onClick = { dayOpen = true }, locked = locked, divider = true, trailing = { Text(dayName(review.day), style = Ab.type.small, color = Ab.colors.muted) },
        )
        AbListRow(
            "Time", icon = Icons.Outlined.Schedule, subtitle = "It comes from this time", onClick = { timeOpen = true }, locked = locked, divider = true,
            trailing = { Text(review.time.format(CLOCK), style = Ab.type.small, color = Ab.colors.muted) },
        )
        Column(Modifier.padding(horizontal = 4.dp, vertical = 8.dp)) {
            Text("Snooze for", style = Ab.type.body, color = Ab.colors.ink, modifier = Modifier.padding(bottom = 6.dp))
            AbSegmented(listOf("1 day", "2 days"), review.snoozeDays - 1, { if (!locked) GoalSettingsPrefs.setReview(context, review.copy(snoozeDays = it + 1)) })
        }
        AbToggleRow(
            "Run the tidy-up straight after", review.tidyUpAfter, { GoalSettingsPrefs.setReview(context, review.copy(tidyUpAfter = it)) },
            subtitle = "Then the tidy-up · about 2 minutes", enabled = !locked,
        )
    }

    if (dayOpen) {
        AbSheet(title = "Review day", subtitle = "Weekly review", onDismiss = { dayOpen = false }, height = AbSheetHeight.Tall) {
            days.forEach { day ->
                AbSheetRow(dayName(day), onClick = { GoalSettingsPrefs.setReview(context, review.copy(day = day)); dayOpen = false }, trailing = { AbSheetCheck(day == review.day) })
            }
        }
    }
    if (timeOpen) {
        // Always 12-hour, whatever the phone's setting.
        val picker = rememberTimePickerState(initialHour = review.time.hour, initialMinute = review.time.minute, is24Hour = false)
        androidx.compose.material3.AlertDialog(
            onDismissRequest = { timeOpen = false },
            containerColor = Ab.colors.surface,
            confirmButton = { AbTextButton("OK", onClick = { GoalSettingsPrefs.setReview(context, review.copy(time = LocalTime.of(picker.hour, picker.minute))); timeOpen = false }) },
            dismissButton = { AbTextButton("Cancel", onClick = { timeOpen = false }) },
            text = { TimePicker(state = picker, colors = abTimePickerColors()) },
        )
    }
}

@Composable
private fun ReminderDefaultsCard() {
    val context = LocalContext.current
    val defaults by GoalSettingsPrefs.reminders.collectAsStateWithLifecycle()
    val locked = LocalGuardsLocked.current
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
        AbSectionLabel("Default reminders", Modifier.padding(start = 4.dp, bottom = 4.dp))
        Text(
            "What a new goal reminder starts with. Suggestions only: nothing locks or waits.",
            style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(start = 4.dp, bottom = 6.dp),
        )
        AbToggleRow(
            "Skip when the goal is already done", defaults.skipWhenDone, { GoalSettingsPrefs.setReminders(context, defaults.copy(skipWhenDone = it)) }, enabled = !locked,
        )
        AbToggleRow(
            "Nudge when the week falls behind · AccountabiliBuddy", defaults.nudgeAccountabiliBuddy,
            { GoalSettingsPrefs.setReminders(context, defaults.copy(nudgeAccountabiliBuddy = it)) },
            subtitle = "One nudge the morning after you drop below pace", enabled = !locked,
        )
        AbToggleRow(
            "Nudge when the week falls behind · ProductaBuddy", defaults.nudgeProductaBuddy,
            { GoalSettingsPrefs.setReminders(context, defaults.copy(nudgeProductaBuddy = it)) }, subtitle = "Off by default in ProductaBuddy mode", enabled = !locked,
        )
    }
}

@Composable
private fun ChartsCard() {
    val context = LocalContext.current
    val display by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val locked = LocalGuardsLocked.current
    val starts = listOf(GoalWeekStart.Monday, GoalWeekStart.Sunday, GoalWeekStart.Saturday)
    val labels = GoalHeatmapLabels.entries
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
        AbSectionLabel("Charts", Modifier.padding(start = 4.dp, bottom = 4.dp))
        Column(Modifier.padding(horizontal = 4.dp, vertical = 8.dp)) {
            Text("Week starts on", style = Ab.type.body, color = Ab.colors.ink, modifier = Modifier.padding(bottom = 6.dp))
            AbSegmented(listOf("Mon", "Sun", "Sat"), starts.indexOf(display.weekStart), { if (!locked) GoalDisplayPrefs.setWeekStart(context, starts[it]) })
        }
        Column(Modifier.padding(horizontal = 4.dp, vertical = 8.dp)) {
            Text("Heatmap day labels", style = Ab.type.body, color = Ab.colors.ink, modifier = Modifier.padding(bottom = 6.dp))
            AbSegmented(labels.map { it.label }, labels.indexOf(display.heatmapLabels), { if (!locked) GoalDisplayPrefs.setHeatmapLabels(context, labels[it]) })
        }
        AbToggleRow(
            "Extra tiles on goal pages", display.extraTiles, { GoalDisplayPrefs.setExtraTiles(context, it) },
            subtitle = "Streak, Total · otherwise in Stats; a goal's own switch wins", enabled = !locked,
        )
    }
}

@Composable
private fun AreasAndConnections(onOpenConnections: () -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val locked = LocalGuardsLocked.current
    var areasOpen by rememberSaveable { mutableStateOf(false) }
    var areaSheet by rememberSaveable { mutableStateOf<String?>(null) }
    var note by rememberSaveable { mutableStateOf<String?>(null) }
    val count = lists.liveAreas.size
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
        AbListRow(
            "Areas", icon = Icons.Outlined.Home, subtitle = "Rename, pick icons, reorder, add", locked = locked,
            onClick = { areasOpen = true }, divider = true, trailing = { Text(if (count == 1) "1 area" else "$count areas", style = Ab.type.small, color = Ab.colors.muted) },
        )
        AbListRow(
            "Connections", icon = Icons.Outlined.Link, subtitle = "Connect Beeminder, Health Connect and your calendars once; pick what counts per goal",
            onClick = onOpenConnections,
        )
    }
    note?.let { Text(it, style = Ab.type.small, color = Ab.colors.muted) }
    if (areasOpen) {
        GoalAreasSheet(onDismiss = { areasOpen = false }, onEdit = { areaSheet = it.toString() }, onNew = { areaSheet = "new" }, onMessage = { note = it })
    }
    areaSheet?.let { raw ->
        GoalAreaSheet(areaId = runCatching { UUID.fromString(raw) }.getOrNull(), onDismiss = { areaSheet = null }, onMessage = { note = it })
    }
}

@Composable
private fun PausingCard() {
    AbCard(contentPadding = PaddingValues(horizontal = 18.dp, vertical = 14.dp), verticalSpacing = 6.dp) {
        AbSectionLabel("Pausing a goal")
        Text(
            "Pausing a goal (from its menu) applies at once: it stops counting toward This week, stops its reminders and nudges, and keeps all " +
                "its history. Paused days are neither hit nor missed, so streaks and pace aren't punished. Turning it back on counts from that " +
                "day. Deep work can't be paused or deleted. Its daily target is set on your PC; lowering its weekly target waits and never " +
                "happens during a session.",
            style = Ab.type.small, color = Ab.colors.muted,
        )
    }
}

private fun dayName(day: DayOfWeek?): String = day?.let { it.getDisplayName(java.time.format.TextStyle.FULL, Locale.US) } ?: "Week's first day"
