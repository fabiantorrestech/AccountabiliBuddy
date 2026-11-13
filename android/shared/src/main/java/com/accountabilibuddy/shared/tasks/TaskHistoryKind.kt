package com.accountabilibuddy.shared.tasks

/** Why a task is in History (ADR 76; the PC's TaskHistoryKind). [wire] is the name the sync file carries. */
enum class TaskHistoryKind(val wire: String) {
    FINISHED("finished"),
    ARCHIVED("archived"),
    DELETED("deleted"),
}
