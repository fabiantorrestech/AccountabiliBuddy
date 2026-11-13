package com.accountabilibuddy.shared.tasks

/** What the phone's task store does at start with its stored lists ([TaskModelStartup.open]; R7.2 review item 2). */
sealed interface TaskStoreOpening {
    /** Counts and states only, never text. */
    val logLine: String

    /**
     * The lists are read: [parts] is what the store holds from now on. [write]: they changed (a set-aside copy was merged in), so they
     * are written back, and with [clearSetAside] the set-aside copy goes in the same commit.
     */
    data class Open(val parts: TaskParts, val write: Boolean, val clearSetAside: Boolean, override val logLine: String) : TaskStoreOpening

    /**
     * The stored lists can't be read: the store stays closed (nothing shown, kept, sent or backed up from it; the old stores are not
     * used either, as they stopped at the migration). With [setAside] the unreadable value is copied aside first; an existing copy is
     * never overwritten.
     */
    data class Closed(val setAside: Boolean, override val logLine: String) : TaskStoreOpening
}
