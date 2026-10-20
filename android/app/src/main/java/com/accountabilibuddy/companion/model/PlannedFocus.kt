package com.accountabilibuddy.companion.model


/** Time the day plan watches for drift: one Watch part (Work, Extra Work…) on a real date, with its category's colour. */
data class PlannedFocus(val name: String, val argb: Long, val window: LocalWindow)
