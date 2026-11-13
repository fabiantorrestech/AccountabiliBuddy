package com.accountabilibuddy.shared.tasks

/** One part of a task row's detail line ([TaskRowText.detail]); [late] parts ("Overdue 3 d") are drawn as an amber pill. */
data class TaskRowPart(val text: String, val late: Boolean = false)
