package com.accountabilibuddy.companion.ui.kit

import androidx.compose.material3.DatePickerColors
import androidx.compose.material3.DatePickerDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.TimePickerColors
import androidx.compose.material3.TimePickerDefaults
import androidx.compose.runtime.Composable
import com.accountabilibuddy.companion.ui.theme.Ab

/** Material's date picker in the theme's tones: the chosen day is ink on a tonal ground, never the accent. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun abDatePickerColors(): DatePickerColors {
    val colors = Ab.colors
    return DatePickerDefaults.colors(
        containerColor = colors.surface,
        titleContentColor = colors.muted,
        headlineContentColor = colors.ink,
        weekdayContentColor = colors.muted,
        subheadContentColor = colors.muted,
        navigationContentColor = colors.ink,
        yearContentColor = colors.ink,
        currentYearContentColor = colors.ink,
        selectedYearContentColor = colors.bg,
        selectedYearContainerColor = colors.ink,
        dayContentColor = colors.ink,
        disabledDayContentColor = colors.faint,
        selectedDayContentColor = colors.bg,
        selectedDayContainerColor = colors.ink,
        todayContentColor = colors.ink,
        todayDateBorderColor = colors.muted,
    )
}

/** Material's time picker in the theme's tones: the hand and the chosen number are ink, never the accent. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun abTimePickerColors(): TimePickerColors {
    val colors = Ab.colors
    return TimePickerDefaults.colors(
        clockDialColor = colors.surface2,
        clockDialSelectedContentColor = colors.bg,
        clockDialUnselectedContentColor = colors.ink,
        selectorColor = colors.ink,
        containerColor = colors.surface,
        periodSelectorBorderColor = colors.line,
        periodSelectorSelectedContainerColor = colors.ink,
        periodSelectorUnselectedContainerColor = colors.surface,
        periodSelectorSelectedContentColor = colors.bg,
        periodSelectorUnselectedContentColor = colors.muted,
        timeSelectorSelectedContainerColor = colors.surface2,
        timeSelectorUnselectedContainerColor = colors.surface2,
        timeSelectorSelectedContentColor = colors.ink,
        timeSelectorUnselectedContentColor = colors.muted,
    )
}
