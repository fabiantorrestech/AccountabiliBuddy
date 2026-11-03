package com.accountabilibuddy.companion.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import java.time.LocalDate

/** Settings → Guards → Phone guard: the daily allowance for "I need my phone for a bit" (the apps are in Essentials). */
@Composable
internal fun EmergencySection() = GuardsScope { EmergencySectionBody() }

@Composable
private fun EmergencySectionBody() {
    val context = LocalContext.current
    val store = (context.applicationContext as CompanionApp).emergency
    var daily by remember { mutableStateOf(store.dailyMinutes) }
    val left = store.minutesLeft(LocalDate.now())

    Section("Emergency phone time", summary = "$daily min a day · $left left today") {
        GuardsRestNote()
        Note("\"I need my phone for a bit\" (in Not working today, and on the parked notice): a reason and 5–60 minutes without the work card or nudges, then the essentials screen; shown on the PC apart from pickups.")
        AbSectionLabel("Minutes a day")
        AbChipRow {
            listOf(15, 30, 45, 60, 90).forEach { minutes ->
                GuardChip("$minutes", selected = daily == minutes, onClick = { store.dailyMinutes = minutes; daily = minutes })
            }
        }
    }
}
