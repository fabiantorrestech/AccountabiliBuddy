package com.accountabilibuddy.shared.tasks

/** The old sync field a bin's tasks travel in for apps older than ADR 76 (the PC's OldTaskList, in its order). */
enum class OldTaskList {
    /** `nextTasks`: This session, Queued, Finished today. */
    NEXT_TASKS,

    /** `later`: Next and Later. */
    LATER,

    /** `parked` (and the legacy `comebacks`): To sort. */
    PARKED,

    /** `autoSaved` (ADR 72): open To sort tasks that came from Auto-saved, within its 30 days. */
    AUTO_SAVED,
}
