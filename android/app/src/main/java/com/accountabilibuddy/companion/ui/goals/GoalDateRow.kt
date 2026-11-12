package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.kit.abDatePickerColors
import com.accountabilibuddy.shared.goals.GoalTexts
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset

/**
 * "By Sun 1/31": the date the long-term target is for (optional; a date needs a long-term amount, so the button is off until one is
 * typed). [onPick] gets the new date, or null from "Clear".
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GoalDateRow(date: LocalDate?, onPick: (LocalDate?) -> Unit, enabled: Boolean) {
    var picking by rememberSaveable { mutableStateOf(false) }
    val today = LocalDate.now()
    Row(Modifier.fillMaxWidth().padding(horizontal = 24.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        AbValueButton(date?.let { "By ${GoalTexts.date(it, today)}" } ?: "Pick a date (optional)", onClick = { picking = true }, enabled = enabled, icon = Icons.Outlined.CalendarMonth)
        if (date != null) AbTextButton("Clear", onClick = { onPick(null) })
    }
    if (picking) {
        val state = rememberDatePickerState(initialSelectedDateMillis = (date ?: today).atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
        DatePickerDialog(
            onDismissRequest = { picking = false },
            confirmButton = {
                AbTextButton("Set date", onClick = {
                    picking = false
                    state.selectedDateMillis?.let { onPick(Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate()) }
                })
            },
            dismissButton = { AbTextButton("Cancel", onClick = { picking = false }) },
            colors = abDatePickerColors(),
        ) {
            DatePicker(state, colors = abDatePickerColors())
        }
    }
}
