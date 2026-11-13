package com.accountabilibuddy.shared.tasks

/**
 * A task entry holds an enum value this version doesn't know (a newer app's bin, History kind or reminder status). Not damage:
 * [TaskJson] keeps such an entry as written instead of using it.
 */
class TaskNewerValueException(message: String) : IllegalArgumentException(message)
