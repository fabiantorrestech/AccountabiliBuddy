package com.accountabilibuddy.companion.stats

import java.time.LocalDate

/** One line of the list under the Overview chart ("Mon, Oct 26", "2h 15m · goal met"); tapping it opens that day. */
data class StatsDayRow(val date: LocalDate, val title: String, val detail: String)
