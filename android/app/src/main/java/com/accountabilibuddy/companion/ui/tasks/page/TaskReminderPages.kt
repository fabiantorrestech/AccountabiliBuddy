package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.abDatePickerColors
import com.accountabilibuddy.companion.ui.kit.abTimePickerColors
import com.accountabilibuddy.companion.ui.sheets.AbSheetCheck
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskReminder
import com.accountabilibuddy.shared.tasks.TaskReminderRings
import com.accountabilibuddy.shared.tasks.TaskRowText
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZoneOffset

/** The relative rules a reminder can have, in the order the sheet offers them (the PC's reminder picker). */
private val RELATIVE_RULES = listOf(
    TaskReminder.AT_START to "At the time",
    TaskReminder.TEN_MINUTES_BEFORE to "10 min before",
    TaskReminder.ONE_HOUR_BEFORE to "1 hr before",
    TaskReminder.DAY_BEFORE_AT_NINE to "The day before, 9:00 AM",
)

/** Which small dialog the Reminders page has open. */
private enum class RemindersDialog { DueDate, DueTime, ReminderDate, ReminderTime }

/**
 * The ⋮ sheet's Reminders & due date page (R7.4; was "on your PC for now"): "Done by" (a date, a time or all day, or none) and the
 * task's reminders (at most [TaskReminder.MAX_PER_TASK]; a relative rule once, or a moment of your own), through [controller] and the
 * rules in [com.accountabilibuddy.shared.tasks.TaskReminderEdits]. The line that says whether this phone rings stays. The calendar
 * copy ("Add new reminders to Google") is the PC's: nothing here touches it.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TaskRemindersPage(task: TaskItem, scopeView: com.accountabilibuddy.shared.tasks.TaskScopeView, self: String, zone: ZoneId, controller: ShelfController) {
    val today = LocalDate.now(zone)
    val due = task.dueOn
    var dialog by remember { mutableStateOf<RemindersDialog?>(null) }
    var pickedDate by remember { mutableStateOf<LocalDate?>(null) }
    var refused by remember { mutableStateOf(false) }

    AbSheetRow(
        "Done by", onClick = { dialog = RemindersDialog.DueDate }, icon = Icons.Outlined.CalendarMonth,
        subtitle = if (due == null) "No due date" else TaskRowText.due(due, task.dueAt, today), trailing = { AbSheetValue(null) },
    )
    if (due != null) {
        AbSheetRow(
            "Time", onClick = { dialog = RemindersDialog.DueTime }, icon = Icons.Outlined.Schedule,
            trailing = { AbSheetValue(task.dueAt?.let { TaskRowText.time(it) } ?: "All day") },
        )
        AbSheetRow("Remove the due date", onClick = { controller.setDue(task.id, null, null) }, icon = Icons.Outlined.Close)
    }

    val reminders = task.reminders.orEmpty()
    if (reminders.isEmpty()) {
        AbSheetRow("No reminders", onClick = null, icon = Icons.Outlined.Notifications)
    } else {
        reminders.forEach { reminder ->
            AbSheetRow(
                TaskRowText.reminder(task.copy(reminders = listOf(reminder)), today, zone) ?: "Reminder", onClick = null, icon = Icons.Outlined.Notifications,
                trailing = {
                    IconButton(onClick = { controller.removeReminder(task.id, reminder.id) }) {
                        Icon(Icons.Outlined.Close, contentDescription = "Remove this reminder", tint = Ab.colors.muted)
                    }
                },
            )
        }
    }

    if (reminders.size < TaskReminder.MAX_PER_TASK) {
        SheetSectionHead("Add a reminder")
        RELATIVE_RULES.forEach { (rule, text) ->
            val has = reminders.any { it.rule == rule }
            AbSheetRow(
                text, onClick = { refused = !controller.addReminder(task.id, rule, null) }, icon = Icons.Outlined.Add, enabled = !has && due != null,
                subtitle = when {
                    has -> "Already set"
                    due == null -> "Needs a due date"
                    else -> null
                },
            )
        }
        AbSheetRow("A date and time of your own…", onClick = { dialog = RemindersDialog.ReminderDate }, icon = Icons.Outlined.Add)
    } else {
        SheetFoot("A task has at most ${TaskReminder.MAX_PER_TASK} reminders. Remove one to add another.")
    }
    if (refused) SheetFoot("That reminder wasn't added: the same one is already set.")

    // "Rings on": whether this phone is among the devices that ring for this task's reminders (Settings › Tasks sets the lists).
    scopeView.ringsLine(task, self)?.let { rings ->
        AbSheetRow(rings, onClick = null, icon = Icons.Outlined.Devices, subtitle = "Rings on is set in Settings › Tasks, for every task and per label")
    }
    SheetFoot("The phone's reminders go off on the devices Rings on names. Adding a task to Google Calendar is chosen on your PC.")

    when (dialog) {
        RemindersDialog.DueDate -> {
            val state = rememberDatePickerState(initialSelectedDateMillis = (due ?: today).atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
            DatePickerDialog(
                onDismissRequest = { dialog = null },
                confirmButton = {
                    AbTextButton("Set", onClick = {
                        dialog = null
                        state.selectedDateMillis?.let { controller.setDue(task.id, Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate(), task.dueAt) }
                    })
                },
                dismissButton = { AbTextButton("Cancel", onClick = { dialog = null }) },
                colors = abDatePickerColors(),
            ) { DatePicker(state, colors = abDatePickerColors()) }
        }

        RemindersDialog.DueTime -> {
            // Always 12-hour, whatever the phone's setting.
            val start = task.dueAt ?: LocalTime.of(9, 0)
            val picker = rememberTimePickerState(initialHour = start.hour, initialMinute = start.minute, is24Hour = false)
            androidx.compose.material3.AlertDialog(
                onDismissRequest = { dialog = null },
                containerColor = Ab.colors.surface,
                confirmButton = { AbTextButton("Set", onClick = { dialog = null; controller.setDue(task.id, due, LocalTime.of(picker.hour, picker.minute)) }) },
                dismissButton = {
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        AbTextButton("All day", onClick = { dialog = null; controller.setDue(task.id, due, null) })
                        AbTextButton("Cancel", onClick = { dialog = null })
                    }
                },
                text = { TimePicker(state = picker, colors = abTimePickerColors()) },
            )
        }

        RemindersDialog.ReminderDate -> {
            val state = rememberDatePickerState(initialSelectedDateMillis = (due ?: today).atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
            DatePickerDialog(
                onDismissRequest = { dialog = null },
                confirmButton = {
                    AbTextButton("Next: the time", onClick = {
                        pickedDate = state.selectedDateMillis?.let { Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate() }
                        dialog = if (pickedDate != null) RemindersDialog.ReminderTime else null
                    })
                },
                dismissButton = { AbTextButton("Cancel", onClick = { dialog = null }) },
                colors = abDatePickerColors(),
            ) { DatePicker(state, colors = abDatePickerColors()) }
        }

        RemindersDialog.ReminderTime -> {
            val picker = rememberTimePickerState(initialHour = 9, initialMinute = 0, is24Hour = false)
            androidx.compose.material3.AlertDialog(
                onDismissRequest = { dialog = null },
                containerColor = Ab.colors.surface,
                confirmButton = {
                    AbTextButton("Add", onClick = {
                        val day = pickedDate
                        dialog = null
                        if (day != null) {
                            val at = day.atTime(picker.hour, picker.minute).atZone(zone).toOffsetDateTime()
                            refused = !controller.addReminder(task.id, TaskReminder.ABSOLUTE, at)
                        }
                    })
                },
                dismissButton = { AbTextButton("Cancel", onClick = { dialog = null }) },
                text = { TimePicker(state = picker, colors = abTimePickerColors()) },
            )
        }

        null -> Unit
    }
}

/** The ⋮ sheet's Sync page (R7.4; was "chosen on your PC for now"): everywhere, or only this phone (a local task, ADR 72). */
@Composable
fun TaskSyncPage(task: TaskItem, self: String, controller: ShelfController) {
    val onlyHere = task.devices?.let { it.size == 1 && it[0].equals(self, ignoreCase = true) } == true
    val subset = task.devices != null && !onlyHere
    AbSheetRow(
        "All devices", onClick = { controller.setOnlyThisDevice(task.id, false) }, icon = Icons.Outlined.Devices, role = Role.RadioButton,
        subtitle = "Syncs to every device in your group", trailing = { AbSheetCheck(task.devices == null) },
    )
    AbSheetRow(
        "This phone only", onClick = { controller.setOnlyThisDevice(task.id, true) }, icon = Icons.Outlined.Devices, role = Role.RadioButton,
        subtitle = "Stays on this phone: other devices keep only a placeholder", trailing = { AbSheetCheck(onlyHere) },
    )
    if (subset) AbSheetRow(TaskRowText.devices(task, self), onClick = null, icon = Icons.Outlined.Devices, subtitle = "Chosen on another device; picking one of the above replaces it")
    SheetFoot("A task kept on this phone only is not sent to your other devices. Syncing it again sends it everywhere.")
}
