package com.accountabilibuddy.shared.tasks

/**
 * Whether this phone rings for a task's reminders (round 2 R7.3b; M7 T2's rule, [TaskRingDevices]): a phone rings only when both
 * "Rings on" lists name it, the account's ([SyncTaskAccountSettings.ringOn]) and the task's label's ([TaskLabel.ringOn]); no list
 * means every device. A task with no label, or one whose label is deleted, follows the account's list alone.
 *
 * The phone's Tasks page says so wherever it shows a task's reminders, and [ringing] is the only list the phone's reminder alarms
 * may be set from, so a reminder never rings on a phone the lists leave out.
 */
object TaskReminderRings {
    /** Whether [device] (a phone or tablet) rings for the reminders of [task]. */
    fun ringsHere(lists: TaskLists, task: TaskItem, device: String): Boolean {
        val label = task.labelId?.let { id -> lists.labels.map { it.label }.firstOrNull { it.isLive && it.id.equals(id, ignoreCase = true) } }
        return TaskRingDevices.ringsFor(lists.account?.ringOn, label?.ringOn, device, isPc = false)
    }

    /** The open tasks with a reminder that rings on [device]. */
    fun ringing(lists: TaskLists, device: String): List<TaskItem> =
        lists.tasks.map { it.task }.filter { it.isLive && !it.isDone && !it.reminders.isNullOrEmpty() && ringsHere(lists, it, device) }

    /** The line under a task's reminders: null without reminders, else whether they ring here. */
    fun line(lists: TaskLists, task: TaskItem, device: String): String? = when {
        task.reminders.isNullOrEmpty() -> null
        ringsHere(lists, task, device) -> "Rings on this phone"
        else -> "Doesn't ring on this phone (Rings on)"
    }
}
