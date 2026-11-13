package com.accountabilibuddy.shared.tasks

/**
 * What the phone's one-time migration ([TaskModelStartup.migrate]) needs from the app: the stores, the backups and the log. The app's
 * side (`PhoneTaskModelMigration`) reads prefs and the Keystore; a check gives a fake, so the order of the steps and every refusal is
 * checked on the JVM.
 */
interface TaskMigrationPort {
    /** This app's version: the sealed copies are kept per version. */
    fun version(): String

    /** The old stores as stored (see [TaskOldStore]). */
    fun oldStores(): List<TaskOldStore>

    /**
     * The sealed values of the old stores named in [names] (they can't be read), copied into the task model's prefs once each: a copy
     * already kept is never overwritten (the old screens, reading such a store as empty, may write over it later).
     */
    fun keepUnreadable(names: List<String>)

    /** Whether this version already wrote its folder backup (the usual backups by kind: once per version, they are large). */
    fun folderBackedUp(version: String): Boolean

    /** The old stores' sealed copies, written now (a fresh copy on every attempt) with this version as attempted; false when not kept. */
    fun recordBackup(version: String): Boolean

    /** The usual backups by kind, when a backup folder is chosen (best effort). */
    fun folderBackup()

    /** The migration rule run on the old stores, and the switch's decision. */
    fun switchOutcome(): TaskModelSwitch.Outcome

    /** The lists and the marker in one commit; false when not kept. */
    fun activate(lists: TaskLists, viewSearches: Map<String, String>): Boolean

    /** A line in the task model log: counts and states only, never text. */
    fun log(line: String)
}
