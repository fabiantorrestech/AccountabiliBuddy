package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime

/** A history file in the shared folder or on the relay and when it was last changed there. */
data class GoalHistoryListing(val key: GoalHistoryFileKey, val modifiedAt: OffsetDateTime)
