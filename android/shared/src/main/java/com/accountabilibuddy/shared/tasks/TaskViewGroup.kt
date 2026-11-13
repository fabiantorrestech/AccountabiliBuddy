package com.accountabilibuddy.shared.tasks

/**
 * One group of a view (the PC's TaskViewGroup): a bin, a label, a tag or everything. [key] is "bin:next", "label:<id>", "label:none",
 * "tag:<lower case tag>", "tag:none" or "all": the same keys the PC stores its column order under.
 */
data class TaskViewGroup(val key: String, val title: String, val bin: TaskBin?, val labelId: String?, val tag: String?, val tasks: List<TaskItem>)
