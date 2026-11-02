package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.runtime.Immutable

/**
 * Everything a row of the Tasks pane shows, ready to draw (the text is the user's private text: drawn, never logged).
 *
 * @property line The muted line under the title ("2 of 3 steps left · 50 min"); empty for none.
 * @property number Its place in This session (1, 2, 3); null elsewhere.
 * @property done Drawn ticked: struck through and dimmed.
 * @property isNow The task the PC shows now in the running session.
 * @property sending Ticked here and waiting for its PC to confirm (shown ticked meanwhile).
 * @property readOnly A row an older PC owns (its own come-backs): it can be ticked, nothing else.
 */
@Immutable
data class TaskRowModel(
    val ref: TaskRef,
    val title: String,
    val line: String,
    val category: String?,
    val categoryColor: String?,
    val number: Int? = null,
    val done: Boolean = false,
    val isNow: Boolean = false,
    val sending: Boolean = false,
    val readOnly: Boolean = false,
)
