package com.accountabilibuddy.companion.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.NightKind
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel

/**
 * Settings → Smart bedtime (ADR 34): a switch per kind of night, the minimum and maximum sleep, and what it learned.
 * It learns from Health Connect sleep and the phone's last use before it, then picks tonight's bedtime from tomorrow's
 * wake time, 15 minutes earlier after each week made on most nights.
 */
@Composable
internal fun SmartBedtimeSection(clock: ClockFormat, needs: @Composable () -> Unit = {}) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val service = app.smartBedtime
    // Bumped on every change, so the section reads the store again.
    var version by remember { mutableIntStateOf(0) }
    fun changed() {
        version++
        service.refresh()
        app.refreshSoon()
    }

    val sleep = app.store.state.collectAsState().value.sleep
    fun save(settings: com.accountabilibuddy.companion.model.SleepSettings) {
        app.sleepSync.saveEdit(settings)
        changed()
    }
    val on = NightKind.entries.filter { sleep.smartOn(it) }
    Section("Smart bedtime", summary = if (on.isEmpty()) "Off" else on.joinToString(" · ") { label(it) }) {
        version.let { } // read so changes redraw
        Note("Learns how long you really sleep (Health Connect) and when you put the phone down, then sets tonight's bedtime from tomorrow's wake time. It starts from what you do now and moves 15 minutes earlier after each week you made it on most nights: first to your minimum, then toward your goal.")
        NightKind.entries.forEach { kind ->
            val on = sleep.smartOn(kind)
            ToggleRow(label(kind), if (on) "On" else "Off", on) { value -> save(sleep.withSmart(kind, value)) }
            // What it learned, in full (not behind ⓘ): the point of the switch.
            if (on) Note(service.status(kind, clock))
        }
        needs()
        AbSectionLabel("Minimum sleep")
        AbChipRow {
            listOf(5.5, 6.0, 6.5, 7.0, 7.5).forEach { hours ->
                AbChip(hoursText(hours), selected = sleep.minHours == hours, onClick = { save(sleep.copy(minHours = hours)) })
            }
        }
        AbSectionLabel("Maximum sleep")
        AbChipRow {
            listOf(8.0, 8.5, 9.0, 9.5, 10.0).forEach { hours ->
                AbChip(hoursText(hours), selected = sleep.maxHours == hours, onClick = { save(sleep.copy(maxHours = hours)) })
            }
        }
    }
}

private fun label(kind: NightKind): String = if (kind == NightKind.Workday) "Workdays" else "Off-Days"

private fun hoursText(hours: Double): String = if (hours % 1.0 == 0.0) "${hours.toInt()}h" else "${hours.toInt()}h 30"
