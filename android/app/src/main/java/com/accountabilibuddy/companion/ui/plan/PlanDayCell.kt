package com.accountabilibuddy.companion.ui.plan

import java.time.LocalDate

/** One day of the week strip: weekday, date number and a short day type ("Work", "Off"). */
data class PlanDayCell(
    val date: LocalDate,
    val weekday: String,
    val number: String,
    val type: String,
    val isToday: Boolean,
    val isSelected: Boolean,
) {
    /** What a screen reader says: "Tuesday 27, Work". */
    val description: String get() = "${date.dayOfWeek.getDisplayName(java.time.format.TextStyle.FULL, java.util.Locale.US)} ${date.dayOfMonth}, $type" +
        (if (isToday) ", today" else "")
}
