package com.accountabilibuddy.shared.tasks

/**
 * One of the phone's old saved filters as the migration reads it (the phone's `TaskFilterPreset`): its id, name, the category chip
 * (a name; null for All) and the search words. It becomes a saved view; the search words stay on the phone.
 */
data class TaskMigrationPreset(val id: String, val name: String, val category: String?, val search: String)
