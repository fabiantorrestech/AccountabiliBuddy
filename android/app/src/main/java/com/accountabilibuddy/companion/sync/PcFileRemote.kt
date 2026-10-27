package com.accountabilibuddy.companion.sync

import android.content.Context
import android.os.SystemClock
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncActionResult
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncEndEarlyOffer
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.Duration
import java.time.Instant
import java.util.UUID

/**
 * Starts and ends a PC's sessions through the sync files (ADR 42, 45): the actions go
 * in this phone's file ([ActionStore]) and the PC answers in its own. Ending early is two steps: "end early" asks, the
 * PC's file brings its offer (its own wait, on its own clock), the phone counts down to the offer's allowed-from time
 * plus 3 seconds and sends the reason's length only; the reason never leaves the phone. The PC is the newest PC file's
 * unless one is picked in Devices (prefs "pcControl").
 */
class PcFileRemote(context: Context, private val actions: ActionStore, private val received: StateFlow<List<ReceivedDevice>>) {
    private val prefs = context.getSharedPreferences("pcControl", Context.MODE_PRIVATE)
    private val _chosenPcId = MutableStateFlow(prefs.getString(KEY_CHOSEN, null))
    private val _news = MutableStateFlow<String?>(null)
    private val _offer = MutableStateFlow<OpenOffer?>(null)

    /** The PC picked in Devices (its sync device id); null: the newest PC file's. */
    val chosenPcId: StateFlow<String?> = _chosenPcId.asStateFlow()

    /** What a PC answered (or why nothing was sent), shown once by the app. */
    val news: StateFlow<String?> = _news.asStateFlow()

    /** The PC's end-early offer while the phone counts it down; null when none is open. */
    val offer: StateFlow<OpenOffer?> = _offer.asStateFlow()

    /** Shows [message] once, like a PC's answer (why a change could not be sent). */
    fun tell(message: String) {
        _news.value = message
    }

    fun clearNews() {
        _news.value = null
    }

    fun choose(pcId: String?) {
        prefs.edit().putString(KEY_CHOSEN, pcId).apply()
        _chosenPcId.value = pcId
    }

    /** The PC this phone controls now; null while no PC file is here. */
    fun target(): ReceivedDevice? = PcFileControl.target(received.value, _chosenPcId.value)

    /** The start or end-early action still waiting for [pc]'s answer; null for none. */
    fun waitingFor(pc: UUID, pending: List<SyncAction> = actions.pending.value): SyncAction? =
        pending.lastOrNull { it.target == pc && it.kind in SyncAction.CONTROL_KINDS }

    /**
     * Asks the PC to start a profile for [minutes]; with no set end ([countUp], explicit: the PC's bar counts up); or, with no
     * [minutes] and no [countUp], a Quick Start that runs for the length the PC remembers and counts down. No profile means the PC's
     * default profile. Null when sent.
     */
    fun start(profileId: String?, minutes: Int?, countUp: Boolean): String? {
        val pc = controllable().getOrElse { return it.message }
        val request = buildJsonObject {
            profileId?.let { put("profileId", it) }
            if (countUp) put("noSetEnd", true) else minutes?.let { put("minutes", it.coerceIn(1, 720)) }
        }
        actions.add(SyncAction.START, pc.file.device.deviceId, text = request.toString())
        return null
    }

    /**
     * Asks the PC [pcId] (its sync device id) to add a task to its running session's checklist (ADR 48). The PC applies the
     * three-tasks rule: a 4th task is parked to Come back to instead (its answer says "Parked: 3 tasks this session", shown
     * in [news]), and it refuses in the last minute of a phase or when no session runs. [taskId] is the id the task gets (so a
     * later [editTask] can name it before the PC's file shows it); [categoryId] is one of the PC's category ids from its file,
     * else [category] (a name, matched or made on the PC, with [categoryColor] "#RRGGBB" for a new one). Several adds may wait
     * at once. Null when sent, else why not.
     */
    fun addTask(
        pcId: UUID,
        title: String,
        categoryId: String? = null,
        category: String? = null,
        categoryColor: String? = null,
        steps: List<String> = emptyList(),
        taskId: UUID = UUID.randomUUID(),
    ): String? {
        val name = title.trim()
        if (name.isEmpty()) {
            return "Type a title first."
        }

        val pc = taskTarget(pcId).getOrElse { return it.message }
        val request = buildJsonObject {
            put("title", name)
            categoryId?.let { put("categoryId", it) }
            category?.trim()?.takeIf { it.isNotEmpty() }?.let { put("category", it) }
            categoryColor?.let { put("categoryColor", it) }
            if (steps.isNotEmpty()) {
                put("steps", JsonArray(steps.map { step -> buildJsonObject { put("title", step.trim()) } }))
            }
        }
        actions.add(SyncAction.TASK_ADD, pc.file.device.deviceId, item = taskId.toString(), text = request.toString())
        return null
    }

    /**
     * Asks the PC [pcId] to rename the task [taskId] of its running session ([title] null keeps it) and/or replace its steps
     * ([steps] null keeps them; a list is the new steps in order: a step with the id of one of its own keeps its tick, any
     * other is new, and steps left out are removed). Ticking a task stays [SyncAction.TICK_TASK]. Null when sent, else why not.
     */
    fun editTask(pcId: UUID, taskId: String, title: String? = null, steps: List<TaskStep>? = null): String? {
        if (title != null && title.isBlank()) {
            return "A task needs a title."
        }

        val pc = taskTarget(pcId).getOrElse { return it.message }
        val request = buildJsonObject {
            title?.let { put("title", it.trim()) }
            steps?.let { list ->
                put("steps", JsonArray(list.map { step ->
                    buildJsonObject {
                        step.id?.let { put("id", it) }
                        put("title", step.title.trim())
                    }
                }))
            }
        }
        actions.add(SyncAction.TASK_EDIT, pc.file.device.deviceId, item = taskId, text = request.toString())
        return null
    }

    /**
     * Asks the PC [pcId] to put its running session's tasks in the order [orderedIds] (task ids, as in its file). The checklist
     * stays grouped by category, so the order counts within each category. Null when sent, else why not.
     */
    fun reorderTasks(pcId: UUID, orderedIds: List<String>): String? {
        if (orderedIds.isEmpty()) {
            return "Nothing to reorder."
        }

        val pc = taskTarget(pcId).getOrElse { return it.message }
        val request = buildJsonObject { put("order", JsonArray(orderedIds.map { JsonPrimitive(it) })) }
        actions.add(SyncAction.TASK_REORDER, pc.file.device.deviceId, text = request.toString())
        return null
    }

    /** The first step of ending early: the PC answers with its wait (or ends a break or a session with no set end at once). Null when sent. */
    fun beginEndEarly(): String? {
        val pc = controllable().getOrElse { return it.message }
        _offer.value = null
        actions.add(SyncAction.END_EARLY_BEGIN, pc.file.device.deviceId)
        return null
    }

    /** The second step, after the countdown: only [reason]'s length goes to the PC. Null when sent. */
    fun endEarly(reason: String): String? {
        val open = _offer.value ?: return "Ask to end the session first."
        if (open.offer.expired(Instant.now())) {
            _offer.value = null
            return TOO_LATE
        }

        if (waitingFor(open.pcId) != null) {
            return "Still waiting for ${open.pcName}'s answer."
        }

        actions.add(SyncAction.END_EARLY, open.pcId, item = open.offer.requestId, text = reason.trim().length.toString())
        return null
    }

    /** "Keep going": the offer is dropped here (the PC's runs out by itself). */
    fun cancelEndEarly() {
        _offer.value = null
    }

    /** Stops waiting for a start or end-early answer (it can't undo one the PC already applied) by taking it out of this phone's file. */
    fun cancelWaiting() {
        val pc = target() ?: return
        waitingFor(pc.file.device.deviceId)?.let { actions.remove(it.id) }
    }

    /**
     * Gives up on start and end-early actions no PC answered in 10 minutes (the PC may be off), so one is never applied
     * much later; an open offer that ran out closes.
     */
    fun expire(now: Instant = Instant.now()) {
        val gone = actions.pending.value.filter {
            (it.kind in SyncAction.CONTROL_KINDS || it.kind in SyncAction.TASK_KINDS) && Duration.between(it.at, now) > GIVE_UP_AFTER
        }
        gone.forEach { actions.remove(it.id) }
        if (gone.isNotEmpty()) {
            val names = gone.map { nameOf(it.target) }.distinct()
            _news.value = "${names.joinToString(" and ")} didn't answer in 10 minutes. Is it on and syncing?"
        }
        _offer.value?.takeIf { it.offer.expired(now) }?.let { _offer.value = null }
    }

    /** A PC acknowledged one of this phone's start or end-early actions ([ActionStore.onAnswered]). */
    fun answered(action: SyncAction, result: SyncActionResult?, pc: ReceivedDevice) {
        if (action.kind in SyncAction.TASK_KINDS) {
            answeredTask(result, pc)
            return
        }

        if (action.kind !in SyncAction.CONTROL_KINDS) {
            return
        }

        val name = pc.file.device.name
        if (result == null || result.status == SyncActionResult.STATUS_UNSUPPORTED) {
            _news.value = "Update AccountabiliBuddy on $name to start and end its sessions from here."
            return
        }

        when (action.kind) {
            SyncAction.START -> _news.value = if (result.ok) "Started on $name." else result.message ?: "$name didn't start it."
            SyncAction.END_EARLY_BEGIN -> when {
                !result.ok -> _news.value = result.message ?: "$name can't end it now."
                // A break or a session with no set end ends at once.
                result.message != null -> _news.value = "$name: ${result.message}"
                else -> {
                    val offer = pc.file.endEarly?.takeUnless { it.expired(Instant.now()) }
                    if (offer == null) {
                        _news.value = "$name's wait ran out before it reached this phone. Ask again."
                    } else {
                        _offer.value = OpenOffer(pc.file.device.deviceId, name, offer)
                    }
                }
            }
            SyncAction.END_EARLY -> {
                // A refusal (say, the phone's clock ran ahead of the wait) keeps a still-valid offer open to try again.
                if (result.ok || _offer.value?.offer?.expired(Instant.now()) != false) {
                    _offer.value = null
                }
                _news.value = if (result.ok) "$name's session ended." else result.message ?: "$name didn't end it."
            }
        }
    }

    // A task change's answer: nothing to say when it was applied as asked; a refusal, or what the PC did instead (a parked 4th task), is shown.
    private fun answeredTask(result: SyncActionResult?, pc: ReceivedDevice) {
        val name = pc.file.device.name
        when {
            result == null || result.status == SyncActionResult.STATUS_UNSUPPORTED -> _news.value = updateTasksText(name)
            !result.ok -> _news.value = result.message ?: "$name didn't change the task."
            result.message != null -> _news.value = "$name: ${result.message}"
        }
    }

    // The PC [pcId] for a task change: its file is here and its app is new enough to apply task changes.
    private fun taskTarget(pcId: UUID): Result<ReceivedDevice> {
        val pc = received.value.firstOrNull { it.file.device.deviceId == pcId && it.file.device.kind == SyncDeviceCard.KIND_PC }
            ?: return Result.failure(IllegalStateException("That PC hasn't synced with this phone yet."))
        if (!PcFileControl.supportsTaskEdits(pc.file.device)) {
            return Result.failure(IllegalStateException(updateTasksText(pc.file.device.name)))
        }

        return Result.success(pc)
    }

    private fun updateTasksText(name: String): String = "Update AccountabiliBuddy on $name to change its session tasks from here."

    // The PC to control, if it can answer and isn't already busy with one of this phone's requests.
    private fun controllable(): Result<ReceivedDevice> {
        val pc = target() ?: return Result.failure(IllegalStateException("No PC has synced with this phone yet."))
        PcFileControl.updateNeeded(pc.file.device)?.let { return Result.failure(IllegalStateException(it)) }
        if (waitingFor(pc.file.device.deviceId) != null) {
            return Result.failure(IllegalStateException("Still waiting for ${pc.file.device.name}'s answer."))
        }

        return Result.success(pc)
    }

    private fun nameOf(pcId: UUID): String = received.value.firstOrNull { it.file.device.deviceId == pcId }?.file?.device?.name ?: "The PC"

    /** One step in [editTask]'s new list: [id] is a step's id from the PC's file, null for a new one. */
    data class TaskStep(val id: String?, val title: String)

    /** An end-early offer from [pcName]'s file, being counted down here. */
    data class OpenOffer(val pcId: UUID, val pcName: String, val offer: SyncEndEarlyOffer) {
        /** The dialog's terms: the wait ends at the offer's allowed-from time on the PC's clock. */
        fun terms(): EndEarlyTerms = EndEarlyTerms(
            waitSeconds = 0, minReasonLength = offer.minReasonLength, note = offer.note,
            receivedAtElapsed = SystemClock.elapsedRealtime(), allowedFrom = offer.allowedFrom,
        )
    }

    companion object {
        private const val KEY_CHOSEN = "chosen"
        private const val TOO_LATE = "Too late — ask to end it again."
        /** How long a request to a PC is worth answering. */
        val GIVE_UP_AFTER: Duration = Duration.ofMinutes(10)
    }
}
