package com.accountabilibuddy.companion.ui.today

import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.sync.PcState
import com.accountabilibuddy.companion.sync.RunningTasks
import com.accountabilibuddy.companion.ui.StepAwayState
import com.accountabilibuddy.companion.ui.TodayPc
import com.accountabilibuddy.companion.ui.tasks.page.ShelfController
import com.accountabilibuddy.shared.session.PhoneAwayKind
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.SessionTasksFromModel
import java.time.Instant
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * The phone's OWN session as Today reads a PC's (ADR 85, PS3a): the one adapter that turns the running [PhoneSessionRecord] into what the session card, the
 * This session card, the session menu and the action bar already read, so a session on this phone looks and moves like a PC's (board B-Phone-Session) and
 * nothing is drawn twice. Pure of Compose except where noted; the times are the record's own (the session's trusted clock, R12).
 */
object OwnSessionView {
    /** The name Today gives the phone's own session, where a PC's would say "Work PC". */
    const val NAME = "this phone"

    /**
     * The record as the PC state the cards read. The phase's start and end are the record's: while the timer is paused ([pausedSince]) the end is NOT moved
     * (it moves on the Resume), so the cards hold the countdown at that moment, as they do for a PC that is stepped away.
     */
    fun pcState(record: PhoneSessionRecord, zone: ZoneId = ZoneId.systemDefault()): PcState = PcState(
        mode = "manual",
        phase = "focus",
        countsAsFocus = true,
        profileId = record.profileId,
        profileName = record.profileName,
        phaseStartedAt = OffsetDateTime.ofInstant(record.phaseStartedAt, zone),
        phaseEndsAt = record.phaseEndsAt?.let { OffsetDateTime.ofInstant(it, zone) },
        profiles = emptyList(),
        countUpProfileId = null,
        tasks = record.checklist.tasks,
        currentTask = record.currentTask,
    )

    /** When the countdown froze (Pause timer, lunch, the phone off); null while it runs, a Step away included (the timer keeps running then, ADR 77). */
    fun pausedSince(record: PhoneSessionRecord): Instant? = record.openAway?.takeIf { it.kind.pausesTimer }?.from

    /** The kind of stretch that is open, for the lines that explain it. */
    fun openKind(record: PhoneSessionRecord): PhoneAwayKind? = record.openAway?.kind

    /** The "this phone" PC the Today parts read for the phone's own session: no file to wait for, nothing to update, the checklist as the session keeps it. */
    fun todayPc(record: PhoneSessionRecord): TodayPc {
        val tasks = record.checklist.tasks.takeIf { it.isNotEmpty() }?.let { RunningTasks(NAME, it, record.currentTask) }
        return TodayPc(NAME, null, pcState(record), pausedSince(record), updateNeeded = null, waiting = null, tasks = tasks)
    }

    /** The line under the timer while a stretch is open: "Away since 2:05 PM", "Paused since 2:05 PM", "Lunch since 12:00 PM". Null when none is open. */
    fun stretchText(record: PhoneSessionRecord, clock: ClockFormat, zone: ZoneId = ZoneId.systemDefault()): String? {
        val open = record.openAway ?: return null
        val since = clock.time(LocalDateTime.ofInstant(open.from, zone))
        return when (open.kind) {
            PhoneAwayKind.STEP_AWAY -> "Away since $since"
            PhoneAwayKind.PAUSED -> "Timer paused since $since"
            PhoneAwayKind.LUNCH -> "Lunch since $since"
            PhoneAwayKind.DEVICE_OFF -> "Phone was off since $since"
        }
    }
}

/** What Today's phone view needs of the phone's own session besides the PC-shaped parts ([OwnSessionView.todayPc]): the record, its tick, and the way to the End sheet. */
class OwnSessionUi(val record: PhoneSessionRecord, val ticker: TaskTicker, val onMessage: (String) -> Unit, val onEnd: () -> Unit)

/**
 * The step-away control of the phone's own session: Step away / I'm back act on the session at once (no PC to ask, no sending state). While the timer is
 * paused (Pause timer, lunch) a Step away is not offered ([StepAwayState.blocked]); "Resume" is in the Session menu.
 */
@Composable
fun rememberOwnStepAway(record: PhoneSessionRecord, clock: ClockFormat, onMessage: (String) -> Unit): StepAwayState {
    val app = LocalContext.current.applicationContext as CompanionApp
    val away = record.openAway?.kind == PhoneAwayKind.STEP_AWAY
    val text = OwnSessionView.stretchText(record, clock)
    val blocked = record.isAway && !away
    return StepAwayState(away, sending = false, text = text, blocked = blocked) {
        val result = if (away) app.phoneSession.controller.back() else app.phoneSession.controller.stepAway()
        refusalText(result)?.let(onMessage)
    }
}

/**
 * The tick of This session's rows for the phone's own session (R9's source): a task goes to Finished and a step is ticked in the shared task model, where the
 * checklist follows ([com.accountabilibuddy.companion.session.PhoneSessionTasks]). The row shows done at once from the model.
 */
@Composable
fun rememberOwnTicker(record: PhoneSessionRecord, onMessage: (String) -> Unit): TaskTicker {
    val app = LocalContext.current.applicationContext as CompanionApp
    val controller = remember(app) { ShelfController(app, sessionFull = null) { toast -> onMessage(toast.message) } }
    val tasks = record.checklist.tasks
    return remember(tasks) {
        TaskTicker(emptySet(), canTick = app.taskModel.isActive) { id ->
            when (val tick = SessionTasksFromModel.tickOf(tasks, id)) {
                is SessionTasksFromModel.Tick.OfTask -> controller.finish(tick.taskId)
                is SessionTasksFromModel.Tick.OfStep -> controller.tickStep(tick.taskId, tick.stepId)
                null -> Unit
            }
        }
    }
}
