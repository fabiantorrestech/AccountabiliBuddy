package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncHistoryTask
import java.time.Instant

/**
 * A task a PC's past session left open (ADR 72), as the phone's Tasks tab lists it under Unfinished: the task, when its session
 * started, the PC it came from, and whether this phone keeps it as a local item.
 */
data class PastTaskRow(val task: SyncHistoryTask, val sessionStart: Instant, val from: String, val keptHere: Boolean)
