package com.accountabilibuddy.shared.tasks

/** What happened to one occurrence of a task reminder (ADR 76; the PC's TaskReminderStatus). [wire] is the name the sync file carries. */
enum class TaskReminderStatus(val wire: String) {
    UP("up"),
    SNOOZED("snoozed"),
    DISMISSED("dismissed"),
    STARTED("started"),
}
