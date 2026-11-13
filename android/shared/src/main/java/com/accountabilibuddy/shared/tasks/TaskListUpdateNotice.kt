package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile

/**
 * "Update AccountabiliBuddy on <PC>" (R7.2 review item 3): a PC whose file shares the old task lists but carries no `tasks` part is
 * older than the one task list (ADR 76), and a phone on the task list ignores its task edits (Q583). The release order is "PC first";
 * this names the PCs that are still behind, so the phone can say so quietly.
 */
object TaskListUpdateNotice {
    /** The names of the PCs among [files] that share old task lists without `tasks`, in the files' order, each once. */
    fun pcsWithoutTasks(files: List<SyncFile>): List<String> = files
        .filter { file ->
            file.device.kind.equals(SyncDeviceCard.KIND_PC, ignoreCase = true) &&
                file.taskParts?.tasks == null &&
                (file.nextTasks != null || file.later != null || file.parked != null)
        }
        .map { it.device.name }
        .distinct()

    /** The quiet line for [names]; null when there is none. */
    fun text(names: List<String>): String? = when (names.size) {
        0 -> null
        1 -> "Update AccountabiliBuddy on ${names[0]}: its task edits don't reach this phone until it does."
        else -> "Update AccountabiliBuddy on ${names.dropLast(1).joinToString(", ")} and ${names.last()}: their task edits don't reach this phone until they do."
    }
}
