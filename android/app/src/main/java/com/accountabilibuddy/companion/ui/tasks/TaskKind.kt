package com.accountabilibuddy.companion.ui.tasks

/**
 * Which list a row of the Tasks pane is from. The pane's groups, and what a row can do, follow it: a next-session task and a
 * Later item are edited on this phone; a running task is the PC's, so its changes are asked of the PC; a ticked task shows under
 * Done today and is put back from the list it came from.
 */
enum class TaskKind {
    /** The next session's task (no session is running). */
    Next,

    /** A task of the running session's checklist, which belongs to its PC. */
    Running,

    /** A come-back (a parked thought). */
    ComeBack,

    /** An item of the Later list. */
    Later,

    /** A next-session task ticked today. */
    DoneNext,

    /** A running session's task ticked. */
    DoneRunning,

    /** A Later item ticked off on this phone today. */
    DoneLater,
}
