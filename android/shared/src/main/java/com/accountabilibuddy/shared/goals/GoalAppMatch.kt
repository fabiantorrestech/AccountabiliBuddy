package com.accountabilibuddy.shared.goals

/** One app or site a screen-time source counts. [text] is private text: sealed at rest, never logged. */
data class GoalAppMatch(val kind: GoalAppMatchKind, val text: String)
