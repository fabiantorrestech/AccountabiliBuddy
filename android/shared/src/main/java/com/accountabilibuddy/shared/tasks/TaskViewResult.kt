package com.accountabilibuddy.shared.tasks

/** What a view shows (the PC's TaskViewResult): its groups in order. A task with two tags is in two tag groups and counts once. */
data class TaskViewResult(val groups: List<TaskViewGroup>) {
    val tasks: List<TaskItem> get() = groups.flatMap { it.tasks }.distinctBy { it.id }
    val count: Int get() = tasks.size
}
