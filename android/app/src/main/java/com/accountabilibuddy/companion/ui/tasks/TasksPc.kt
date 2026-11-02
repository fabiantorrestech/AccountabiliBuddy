package com.accountabilibuddy.companion.ui.tasks

import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.sync.RunningTasks
import java.time.Instant
import java.util.UUID

/**
 * The PC whose session is running, as the Tasks pane needs it: while it exists, This session is that PC's checklist and every
 * change to it is asked of the PC (add, edit, reorder, tick), which applies it under its own rules.
 *
 * @property checklist The running session's tasks (empty when it has none yet).
 * @property canEdit The PC is new enough to apply task adds, edits and reorders (ADR 48).
 */
class TasksPc(val name: String, val pcId: UUID, val checklist: RunningTasks, val canEdit: Boolean) {
    companion object {
        /**
         * The PC Today is about (the controlled PC while it runs a session, else any other linked PC with a live one) when it
         * has a live session, else null: no session is running and the next session's list is the one to show.
         */
        fun of(controlPc: ReceivedDevice?, received: List<ReceivedDevice>, now: Instant, editsAvailable: Boolean): TasksPc? {
            fun isLive(device: ReceivedDevice) = PcFileControl.liveRunning(device.file, now) != null
            val device = controlPc?.takeIf { isLive(it) } ?: PcFileControl.pcs(received).firstOrNull { isLive(it) } ?: return null
            val state = PcFileControl.stateOf(device.file, now)
            val card = device.file.device
            return TasksPc(
                name = card.name,
                pcId = card.deviceId,
                checklist = RunningTasks(card.name, state.tasks, state.currentTask),
                canEdit = editsAvailable && PcFileControl.supportsTaskEdits(card),
            )
        }
    }
}
