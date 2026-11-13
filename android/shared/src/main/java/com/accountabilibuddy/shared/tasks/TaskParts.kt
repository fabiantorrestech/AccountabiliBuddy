package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject

/**
 * ADR 76's parts of a sync file (the PC's SyncFile.Tasks, Labels, Views, TaskLog, TaskAccountSettings), read and cleaned by
 * [TaskJson.readParts]. A null part is one the file doesn't carry (an app that predates it, a QR file, task lists not shared).
 * The `newer...` lists hold entries only a newer app can read, as written ([TaskRead.newer]).
 */
data class TaskParts(
    val tasks: List<SyncTaskItem>?,
    val labels: List<SyncTaskLabel>?,
    val views: List<SyncTaskView>?,
    val taskLog: List<SyncTaskLogItem>?,
    val accountSettings: SyncTaskAccountSettings?,
    val newerTasks: List<JsonObject> = emptyList(),
    val newerLabels: List<JsonObject> = emptyList(),
    val newerViews: List<JsonObject> = emptyList(),
    val newerTaskLog: List<JsonObject> = emptyList(),
)
