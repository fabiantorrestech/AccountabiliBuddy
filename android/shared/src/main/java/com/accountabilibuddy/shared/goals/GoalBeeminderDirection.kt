package com.accountabilibuddy.shared.goals

/**
 * Which way a Beeminder link goes (change note §4). Only [Read] works: [Send] and [Both] are "Coming soon" until ADR 80 is
 * accepted (ADR 43 writes nothing).
 */
enum class GoalBeeminderDirection {
    Read,
    Send,
    Both,
}
