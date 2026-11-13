package com.accountabilibuddy.shared.tasks

/**
 * Rebuild of an unreadable task store (Q640): the sealed copy that can't be opened is set aside, never deleted or overwritten, and the
 * store starts empty; the lists come back from the next sync or a backup (a copy that opens later is merged back in, [TaskModelStartup]).
 * Asks first and is refused during a focus session the user started.
 */
object TaskStoreRebuild {
    /** Why a rebuild isn't possible now; null when it is. */
    fun refusal(isClosed: Boolean, sessionRunning: Boolean): String? = when {
        !isClosed -> "The task list opens fine, so there is nothing to rebuild."
        sessionRunning -> "Not during a focus session."
        else -> null
    }

    /**
     * The sealed copy kept aside after a rebuild: an earlier set-aside copy stays as it is (never overwritten); else the stored one is
     * kept. Null only when there was neither (nothing to keep). The result is never null while either exists.
     */
    fun keptAside(stored: String?, setAside: String?): String? = setAside ?: stored
}
