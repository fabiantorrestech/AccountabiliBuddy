package com.accountabilibuddy.companion.ui

import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Headphones
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Text
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.noise.HeadphoneSchedule
import com.accountabilibuddy.companion.noise.HeadphoneSettings
import com.accountabilibuddy.companion.noise.HeadphoneWatcher
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbCheckRow
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.kit.abTimePickerColors
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/** Settings → Alerts & on-screen → "Play when headphones connect": wired, and the chosen paired Bluetooth headphones. */
@Composable
internal fun HeadphonesSection() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val settings by app.headphones.settings.collectAsState()
    var paired by remember { mutableStateOf(HeadphoneWatcher.pairedAudio(context)) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        paired = HeadphoneWatcher.pairedAudio(context)
        if (granted) {
            app.headphones.save(app.headphones.settings.value.copy(bluetooth = true))
        }
    }
    val needsPermission = Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && paired.isEmpty()
    val summary = listOfNotNull(
        "wired".takeIf { settings.wired },
        settings.devices.takeIf { settings.bluetooth && it.isNotEmpty() }?.joinToString { it.name },
    ).joinToString(" · ").ifEmpty { "Off" }

    Section("Play when headphones connect", summary = summary) {
        Note("The noise player starts its last file when these headphones connect, any time of day, and pauses when they disconnect.")
        AbInfoBanner(
            "Tip: it really helps to have a dedicated pair of work/focus headphones, so putting them on means focus " +
                "and it doesn't get in the way of your normal headphones.",
            icon = Icons.Outlined.Headphones,
        )
        ToggleRow("Wired headphones", "Headphones or a headset in the jack or USB-C", settings.wired) {
            app.headphones.save(settings.copy(wired = it))
        }
        ToggleRow("Bluetooth headphones", "Only the ones ticked below", settings.bluetooth) { on ->
            if (on && needsPermission) {
                permission.launch(Manifest.permission.BLUETOOTH_CONNECT)
            } else {
                app.headphones.save(settings.copy(bluetooth = on))
                paired = HeadphoneWatcher.pairedAudio(context)
            }
        }
        if (settings.bluetooth) {
            val all = paired + settings.devices.filter { chosen -> paired.none { it.address == chosen.address } }
            all.forEach { device ->
                val ticked = settings.devices.any { it.address == device.address }
                AbCheckRow(device.name, checked = ticked, onCheckedChange = { on ->
                    val others = settings.devices.filter { it.address != device.address }
                    app.headphones.save(settings.copy(devices = if (on) others + device else others))
                })
            }
            when {
                all.isEmpty() -> Note("No paired Bluetooth headphones. Pair them in Android's Bluetooth settings, or allow \"Nearby devices\".")
                settings.devices.isEmpty() -> Note("Tick the headphones that should start the noise.")
            }
            AbTonalButton("Look again", onClick = {
                if (needsPermission) permission.launch(Manifest.permission.BLUETOOTH_CONNECT) else paired = HeadphoneWatcher.pairedAudio(context)
            })
        }
        ScheduleOptions(settings) { app.headphones.save(it) }
    }
}

private val DayNames = listOf(1 to "Mon", 2 to "Tue", 3 to "Wed", 4 to "Thu", 5 to "Fri", 6 to "Sat", 7 to "Sun")
private val TimeFormat = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

private fun minuteText(minute: Int): String = LocalTime.of(minute / 60, minute % 60).format(TimeFormat)

/** "Only at certain times": a switch, then the two options; either or both may be ticked. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ScheduleOptions(settings: HeadphoneSettings, save: (HeadphoneSettings) -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val plan = app.store.state.value.document.dayPlan
    var editing by remember { mutableStateOf<Boolean?>(null) } // true = start, false = end
    ToggleRow("Only at certain times", "Off: it starts whenever these headphones connect.", settings.onlyAtTimes) {
        save(settings.copy(onlyAtTimes = it))
    }
    if (!settings.onlyAtTimes) return

    AbCheckRow("During a day plan window", checked = settings.duringPlanWindow, onCheckedChange = { save(settings.copy(duringPlanWindow = it)) })
    if (settings.duringPlanWindow) {
        val chosen = HeadphoneSchedule.chosenCategoryIds(settings, plan)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(start = 16.dp)) {
            plan.categories.filter { it.behavior != CategoryBehavior.Sleep }.forEach { category ->
                AbChip(
                    category.name,
                    selected = category.id in chosen,
                    onClick = { save(settings.copy(planCategoryIds = if (category.id in chosen) chosen - category.id else chosen + category.id)) },
                )
            }
        }
    }

    AbCheckRow("On these days and hours", checked = settings.onDaysAndHours, onCheckedChange = { save(settings.copy(onDaysAndHours = it)) })
    if (settings.onDaysAndHours) {
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(start = 16.dp)) {
            DayNames.forEach { (day, name) ->
                AbChip(
                    name,
                    selected = day in settings.days,
                    onClick = { save(settings.copy(days = if (day in settings.days) settings.days - day else settings.days + day)) },
                )
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(start = 16.dp)) {
            AbValueButton(minuteText(settings.startMinute), onClick = { editing = true })
            Text("to", style = Ab.type.bodyMedium, color = Ab.colors.muted)
            AbValueButton(minuteText(settings.endMinute), onClick = { editing = false })
        }
    }

    if (!settings.duringPlanWindow && !settings.onDaysAndHours) {
        Note("Nothing is ticked, so the noise will never start by itself. Tick at least one option.", color = Ab.colors.waiting)
    }

    editing?.let { isStart ->
        val initial = if (isStart) settings.startMinute else settings.endMinute
        HeadphoneTimeDialog(initial, onDismiss = { editing = null }) { picked ->
            editing = null
            save(if (isStart) settings.copy(startMinute = picked) else settings.copy(endMinute = picked))
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun HeadphoneTimeDialog(initialMinute: Int, onDismiss: () -> Unit, onPick: (Int) -> Unit) {
    // Always 12-hour, whatever the phone's setting.
    val picker = rememberTimePickerState(initialHour = initialMinute / 60, initialMinute = initialMinute % 60, is24Hour = false)
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Ab.colors.surface,
        confirmButton = { AbTextButton("OK", onClick = { onPick(picker.hour * 60 + picker.minute) }) },
        dismissButton = { AbTextButton("Cancel", onClick = onDismiss) },
        text = { TimePicker(state = picker, colors = abTimePickerColors()) },
    )
}
