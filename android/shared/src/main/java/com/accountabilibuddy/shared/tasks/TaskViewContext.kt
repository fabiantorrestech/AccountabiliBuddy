package com.accountabilibuddy.shared.tasks

import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * What [TaskViewRules] needs besides the view (the PC's TaskViewContext): the time and zone "today" is read in, the labels (their
 * order is the label groups' order), the search words, the bins in [scope] (null for all) and whether empty bins still get a group.
 */
data class TaskViewContext(
    val now: OffsetDateTime,
    val zone: ZoneId,
    val labels: List<TaskLabel>,
    val search: String? = null,
    val scope: List<TaskBin>? = null,
    val showEmptyBins: Boolean = false,
)
