package com.accountabilibuddy.companion.ui.tasks.archive

import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.tasks.TasksPc
import com.accountabilibuddy.shared.tasks.ArchiveBuilder
import com.accountabilibuddy.shared.tasks.ArchiveComeback
import com.accountabilibuddy.shared.tasks.ArchiveDeviceNames
import com.accountabilibuddy.shared.tasks.ArchiveInput
import com.accountabilibuddy.shared.tasks.ArchiveItem
import com.accountabilibuddy.shared.tasks.ArchiveList
import com.accountabilibuddy.shared.tasks.ArchiveRows
import com.accountabilibuddy.shared.tasks.TaskLists
import java.time.Instant
import java.time.ZoneId
import java.time.temporal.ChronoUnit

/** The Archive's rows (the shared builder's) and its list lines (the shared assembly's), unfiltered. */
class ArchiveData(val rows: ArchiveRows, val items: List<ArchiveItem>)

/**
 * Builds the Archive from what this phone holds (AR1c): the one task list, the other PCs' histories and the settle records and tasks kept here
 * ([com.accountabilibuddy.companion.sync.SharedListsStore.archive]), the come-backs, every device's notes, and the device names for "from <device>".
 * The phone has no history of its own (it writes no sessions), so [ArchiveInput.own] is empty. Rebuilt when any of them changes and each minute.
 */
@Composable
fun rememberArchiveData(app: CompanionApp, pc: TasksPc?, now: Instant, zone: ZoneId): ArchiveData {
    val taskLists by app.taskModel.lists.collectAsState()
    val sources by app.sharedLists.archive.collectAsState()
    val parked by app.sharedLists.parked.collectAsState()
    val notes by app.sessionNotes.notes.collectAsState()
    val everyDevice by app.sync.allReceived.collectAsState()
    val removed by app.sync.removedDevices.collectAsState()
    val settings by app.sync.settings.collectAsState()
    val minute = now.truncatedTo(ChronoUnit.MINUTES)
    val running = pc?.checklist?.tasks?.mapNotNull { it.id?.lowercase() }?.toSet().orEmpty()
    val self = remember { app.sync.deviceId.toString() }

    val taskListActive = app.taskModel.isActive
    val rows = remember(taskLists, sources, notes, everyDevice, removed, minute, running, taskListActive) {
        val names = ArchiveDeviceNames.of(
            everyDevice.map { it.file.device.deviceId.toString() to it.file.device.name },
            sources.received.map { it.deviceId to it.name },
            removed.map { it.deviceId.toString() to it.name },
        )
        ArchiveBuilder.build(
            ArchiveInput(
                emptyList(), sources.received, sources.kept, sources.settled,
                if (taskListActive) taskLists else TaskLists.EMPTY, minute, running, notes, self, names,
            ),
        )
    }
    val items = remember(rows, parked, settings.shareTaskLists, taskLists, zone) {
        ArchiveList.items(
            rows, parked.map { ArchiveComeback(it.id, it.text, it.createdAt, it.doneAt) }, settings.shareTaskLists,
            categoryName = { id, stored -> taskLists.labels.firstOrNull { it.deletedAt == null && it.id.equals(id, ignoreCase = true) }?.label?.name ?: stored },
            whenText = { ArchiveList.whenText(it, zone) },
        )
    }
    return ArchiveData(rows, items)
}
