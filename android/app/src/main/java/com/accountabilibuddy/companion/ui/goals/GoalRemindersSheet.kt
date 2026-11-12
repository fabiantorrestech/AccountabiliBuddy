package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.TrendingDown
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.DoneAll
import androidx.compose.material.icons.outlined.Repeat
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.abDatePickerColors
import com.accountabilibuddy.companion.ui.kit.abTimePickerColors
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalReminder
import com.accountabilibuddy.shared.goals.GoalReminderEdits
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID

/**
 * Edit goal › Reminders on the phone (round 2 R6b3; not drawn for the phone, so it mirrors the PC's board Goals-PC-Edit-Reminders): the
 * heading with a **PB** badge in ProductaBuddy mode and "Suggestions only: nothing locks or waits. They ring like task reminders."; a
 * numbered list where each reminder has its day pill (Today / Every day / Sun / Fri 12/5, a menu next to it), its time pill (a 12-hour
 * clock), the repeat, the reason (private text, never logged or put in a notification) and × to remove it; "+ Add a reminder"; and the
 * RULES switches, which apply to all of the goal's reminders ("Skip when the goal is already done"; "Nudge when the week falls behind",
 * on by default in AccountabiliBuddy mode, off in ProductaBuddy). Changes are kept in the sheet until [onDone] (Done from Edit goal,
 * or Save when the page was opened from the goal menu or page).
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun GoalRemindersSheet(
    goalId: UUID,
    goalName: String,
    reminders: List<GoalReminder>,
    onReminders: (List<GoalReminder>) -> Unit,
    skipWhenDone: Boolean,
    nudgeWhenBehind: Boolean,
    onRules: (skip: Boolean, nudge: Boolean) -> Unit,
    productaBuddy: Boolean,
    doneLabel: String,
    onDone: () -> Unit,
    onDismiss: () -> Unit,
    onBack: (() -> Unit)?,
) {
    val colors = Ab.colors
    val today = LocalDate.now()
    AbSheet(
        title = "Reminders",
        subtitle = goalName.ifBlank { "Your goal" },
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        onBack = onBack,
        titleAction = if (productaBuddy) ({ PbBadge() }) else null,
        footer = { AbPrimaryButton(doneLabel, onClick = onDone, modifier = Modifier.fillMaxWidth()) },
    ) {
        Text(
            "Suggestions only: nothing locks or waits. They ring like task reminders.",
            style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 6.dp),
        )
        reminders.forEachIndexed { index, reminder ->
            ReminderRow(
                number = index + 1, reminder = reminder, today = today,
                onChange = { changed -> onReminders(reminders.map { if (it.id == changed.id) changed else it }) },
                onRemove = { onReminders(reminders.filter { it.id != reminder.id }) },
            )
        }
        AbSheetRow(
            "Add a reminder", icon = Icons.Outlined.Add,
            onClick = {
                val now = OffsetDateTime.now()
                onReminders(reminders + GoalReminderEdits.newReminder(goalId, reminders.size + 1.0, skipWhenDone, nudgeWhenBehind, now))
            },
        )

        GoalFieldLabel("RULES")
        AbSheetRow(
            "Skip when the goal is already done", onClick = { onRules(!skipWhenDone, nudgeWhenBehind) }, icon = Icons.Outlined.DoneAll, role = Role.Switch,
            subtitle = "Checked when it comes due", trailing = { AbSwitch(skipWhenDone, onCheckedChange = { onRules(it, nudgeWhenBehind) }) },
        )
        AbSheetRow(
            "Nudge when the week falls behind", onClick = { onRules(skipWhenDone, !nudgeWhenBehind) }, icon = Icons.AutoMirrored.Outlined.TrendingDown, role = Role.Switch,
            subtitle = "One nudge the morning after it drops below pace",
            trailing = { AbSwitch(nudgeWhenBehind, onCheckedChange = { onRules(skipWhenDone, it) }) },
        )
        Text(
            if (productaBuddy) "In ProductaBuddy mode a reminder rings once, as a plain notification." else "A reminder rings as a notification; Snooze brings it back in 10 minutes.",
            style = Ab.type.small, color = colors.faint, modifier = Modifier.padding(horizontal = 24.dp, vertical = 10.dp),
        )
    }
}

@Composable
private fun PbBadge() {
    Text(
        "PB", style = Ab.type.micro, fontWeight = FontWeight.SemiBold, color = Ab.colors.muted,
        modifier = Modifier.border(1.dp, Ab.colors.line, Ab.shapes.pill).padding(horizontal = 9.dp, vertical = 2.dp),
    )
}

// One numbered reminder: day pill (menu next to it), time pill (clock), the repeat, the reason, ×.
@OptIn(ExperimentalLayoutApi::class, ExperimentalMaterial3Api::class)
@Composable
private fun ReminderRow(number: Int, reminder: GoalReminder, today: LocalDate, onChange: (GoalReminder) -> Unit, onRemove: () -> Unit) {
    val colors = Ab.colors
    var dayMenu by rememberSaveable(reminder.id) { mutableStateOf(false) }
    var timeOpen by rememberSaveable(reminder.id) { mutableStateOf(false) }
    var dateOpen by rememberSaveable(reminder.id) { mutableStateOf(false) }
    Column(
        Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp).clip(RoundedCornerShape(14.dp)).background(colors.surface2).padding(start = 14.dp, end = 4.dp, top = 8.dp, bottom = 10.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("$number.", style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted)
            FlowRow(Modifier.weight(1f), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Box {
                    Pill(GoalReminderEdits.dayPill(reminder, today), Icons.Outlined.CalendarMonth, "Day: ${GoalReminderEdits.dayPill(reminder, today)}") { dayMenu = true }
                    DropdownMenu(expanded = dayMenu, onDismissRequest = { dayMenu = false }) {
                        GoalReminderEdits.dayChoices(reminder).forEachIndexed { choice, label ->
                            DropdownMenuItem(
                                text = { Text(label, color = colors.ink) },
                                onClick = {
                                    dayMenu = false
                                    if (choice == GoalReminderEdits.ON_A_DATE) dateOpen = true else onChange(GoalReminderEdits.withDayChoice(reminder, choice, today))
                                },
                            )
                        }
                    }
                }
                Pill(GoalReminderEdits.timePill(reminder.time), Icons.Outlined.Schedule, "Time: ${GoalReminderEdits.timePill(reminder.time)}") { timeOpen = true }
                Row(Modifier.heightIn(min = 36.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    Icon(Icons.Outlined.Repeat, contentDescription = null, tint = colors.muted, modifier = Modifier.size(16.dp))
                    Text(GoalReminderEdits.repeatText(reminder), style = Ab.type.small, color = colors.muted)
                }
            }
            IconButton(onClick = onRemove) { Icon(Icons.Outlined.Close, contentDescription = "Remove reminder $number", tint = colors.muted) }
        }
        AbTextField(
            reminder.reason.orEmpty(), { onChange(reminder.copy(reason = it.take(200))) }, "Reason (optional, private), e.g. if not done yet",
            Modifier.padding(end = 10.dp),
        )
    }

    if (timeOpen) {
        // Always 12-hour, whatever the phone's setting.
        val picker = rememberTimePickerState(initialHour = reminder.time.hour, initialMinute = reminder.time.minute, is24Hour = false)
        androidx.compose.material3.AlertDialog(
            onDismissRequest = { timeOpen = false },
            containerColor = colors.surface,
            confirmButton = { AbTextButton("OK", onClick = { onChange(reminder.copy(time = LocalTime.of(picker.hour, picker.minute))); timeOpen = false }) },
            dismissButton = { AbTextButton("Cancel", onClick = { timeOpen = false }) },
            text = { TimePicker(state = picker, colors = abTimePickerColors()) },
        )
    }
    if (dateOpen) {
        val start = reminder.date ?: today
        val state = rememberDatePickerState(initialSelectedDateMillis = start.atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
        DatePickerDialog(
            onDismissRequest = { dateOpen = false },
            confirmButton = {
                AbTextButton("Set date", onClick = {
                    dateOpen = false
                    state.selectedDateMillis?.let { millis ->
                        val picked = Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate()
                        onChange(GoalReminderEdits.withDayChoice(reminder, GoalReminderEdits.ON_A_DATE, today, picked))
                    }
                })
            },
            dismissButton = { AbTextButton("Cancel", onClick = { dateOpen = false }) },
            colors = abDatePickerColors(),
        ) {
            DatePicker(state, colors = abDatePickerColors())
        }
    }
}

@Composable
private fun Pill(text: String, icon: androidx.compose.ui.graphics.vector.ImageVector, description: String, onClick: () -> Unit) {
    val colors = Ab.colors
    androidx.compose.material3.Surface(
        onClick = onClick,
        shape = Ab.shapes.pill,
        color = colors.surface,
        contentColor = colors.ink,
        modifier = Modifier.heightIn(min = 36.dp).semantics { contentDescription = description },
    ) {
        Row(Modifier.padding(horizontal = 12.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Icon(icon, contentDescription = null, tint = colors.muted, modifier = Modifier.size(16.dp))
            Text(text, style = Ab.type.small.copy(fontSize = 14.sp, fontFeatureSettings = "tnum"), fontWeight = FontWeight.Medium)
        }
    }
}
