package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.ChevronLeft
import androidx.compose.material.icons.outlined.ChevronRight
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.SelectableDates
import androidx.compose.material3.Text
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.StatsFormat
import com.accountabilibuddy.companion.stats.StatsPeriod
import com.accountabilibuddy.companion.stats.StatsPeriods
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.kit.abDatePickerColors
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset

/**
 * "‹ Oct 26 – Nov 1 ›" under the range controls: the arrows step one period back or forward (the pane decides how far they can go),
 * and the dates between them jump back to the current period. [label] is already worded; nothing here knows the data.
 */
@Composable
fun StatsPeriodBar(
    label: String,
    canStepBack: Boolean,
    canStepForward: Boolean,
    onBack: () -> Unit,
    onForward: () -> Unit,
    onCurrent: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Row(modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        AbQuietIconButton(Icons.Outlined.ChevronLeft, "Previous period", onClick = onBack, enabled = canStepBack)
        Box(
            Modifier
                .weight(1f)
                .heightIn(min = 44.dp)
                .clickable(role = Role.Button, onClick = onCurrent)
                .semantics { contentDescription = "$label. Go to the current period" },
            contentAlignment = Alignment.Center,
        ) {
            Text(label, style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink, textAlign = TextAlign.Center, maxLines = 1)
        }

        AbQuietIconButton(Icons.Outlined.ChevronRight, "Next period", onClick = onForward, enabled = canStepForward)
    }
}

/**
 * The two dates of a Custom range as buttons, each opening Material's date picker. Only days from [earliest] (the first day the view has
 * anything for, the same one its ‹ arrow stops at) up to today can be picked; if the two come out the wrong way round they swap ([StatsPeriod.custom]).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StatsCustomDates(period: StatsPeriod, earliest: LocalDate, today: LocalDate, onChange: (StatsPeriod) -> Unit, modifier: Modifier = Modifier) {
    // 0 none, 1 the first date, 2 the last date; survives rotation.
    var picking by rememberSaveable { mutableStateOf(0) }
    Row(modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterHorizontally), verticalAlignment = Alignment.CenterVertically) {
        AbValueButton("From ${StatsFormat.shortDate(period.from)}", onClick = { picking = 1 }, icon = Icons.Outlined.CalendarMonth)
        AbValueButton("To ${StatsFormat.shortDate(period.to)}", onClick = { picking = 2 }, icon = Icons.Outlined.CalendarMonth)
    }

    if (picking != 0) {
        val editingFirst = picking == 1
        val shown = if (editingFirst) period.from else period.to
        // Kept across recompositions (the picker's state is keyed on it) and renewed when the day or the first day moves.
        val selectable = remember(earliest, today) {
            object : SelectableDates {
                override fun isSelectableDate(utcTimeMillis: Long): Boolean {
                    val date = Instant.ofEpochMilli(utcTimeMillis).atZone(ZoneOffset.UTC).toLocalDate()
                    return !date.isBefore(earliest) && !date.isAfter(today)
                }
            }
        }
        val state = rememberDatePickerState(
            initialSelectedDateMillis = shown.atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli(),
            selectableDates = selectable,
        )
        DatePickerDialog(
            onDismissRequest = { picking = 0 },
            confirmButton = {
                AbTextButton(if (editingFirst) "Set first day" else "Set last day", onClick = {
                    picking = 0
                    val millis = state.selectedDateMillis ?: return@AbTextButton
                    val date = Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate()
                    val picked = if (editingFirst) StatsPeriod.custom(date, period.to) else StatsPeriod.custom(period.from, date)
                    onChange(StatsPeriods.clamp(picked, earliest, today))
                })
            },
            dismissButton = { AbTextButton("Cancel", onClick = { picking = 0 }) },
            colors = abDatePickerColors(),
        ) {
            DatePicker(state, colors = abDatePickerColors())
        }
    }
}
