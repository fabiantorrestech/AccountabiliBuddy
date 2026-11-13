package com.accountabilibuddy.shared.tasks

/**
 * One of the phone's old task stores as the one-time migration finds it (R7.2 review item 1): [stored] is the sealed value in its
 * prefs (null: nothing stored, which reads as an empty store), [opened] what its Keystore key opened it to (null: it couldn't be
 * opened). Text only goes through [TaskModelStartup.unreadableOldStores]; it is never logged.
 *
 * @property name The store's prefs file and key ("shared_lists/lists"), for the log.
 * @property shape The JSON the store writes at its top level.
 */
data class TaskOldStore(val name: String, val stored: String?, val opened: String?, val shape: Shape) {
    enum class Shape { OBJECT, ARRAY }
}
