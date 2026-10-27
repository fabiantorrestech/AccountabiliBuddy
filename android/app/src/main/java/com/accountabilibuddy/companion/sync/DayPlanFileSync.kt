package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.data.DayPlanJson
import com.accountabilibuddy.companion.data.ProfileDocumentParser
import com.accountabilibuddy.companion.engine.DayPlanChanges
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayPlanConflict
import com.accountabilibuddy.companion.model.PendingDayPlan
import com.accountabilibuddy.companion.model.PendingStatus
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncActionResult
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.put
import java.time.Instant
import java.time.OffsetDateTime

/**
 * Sends a day plan edited on the phone (ADR 42, the transport for ADR 19): it goes to the controlled PC as a
 * "dayPlanEdit" action (one at a time), and the PC's answer in its file is acted on: applied clears the edit, locked
 * keeps it and sends it again (with a new id) once the PC's file shows no focus session, invalid keeps it with a note, a
 * conflict waits for the user's pick, and a late or unanswered one goes again.
 */
class DayPlanFileSync(
    private val store: CompanionStore,
    private val actions: ActionStore,
    private val remote: PcFileRemote,
    private val received: StateFlow<List<ReceivedDevice>>,
    private val scope: CoroutineScope,
) {
    private val _conflict = MutableStateFlow<DayPlanConflict?>(null)

    /** Both the PC and the phone changed the plan; null when there's nothing to decide. */
    val conflict: StateFlow<DayPlanConflict?> = _conflict.asStateFlow()

    /** The PC the open conflict is with, for the dialog. */
    var conflictPcName: String? = null
        private set

    fun start() {
        scope.launch {
            combine(store.state, actions.pending, received) { state, pending, _ -> state.pendingPlan to pending }.collect { trySend() }
        }
    }

    /** "Use this phone's": it goes again on top of the PC's current plan. */
    fun useMine() {
        val conflict = _conflict.value ?: return
        store.rebasePending(conflict.pcPlan, conflict.pcVersion)
        _conflict.value = null
    }

    /** "Keep the PC's": the phone's edit is dropped and the PC's newest plan shows. */
    fun keepPc() {
        _conflict.value ?: return
        store.clearPendingPlan(null)
        _conflict.value = null
    }

    /** A PC acknowledged one of this phone's plan edits ([ActionStore.onAnswered]). */
    fun answered(action: SyncAction, result: SyncActionResult?, pc: ReceivedDevice) {
        if (action.kind != SyncAction.DAY_PLAN_EDIT) {
            return
        }

        val sent = Sent.read(action.text) ?: return
        val name = pc.file.device.name
        when (result?.status) {
            SyncActionResult.STATUS_APPLIED -> store.finishPendingPlan(sent.editedAt, sent.plan, result.planVersion)
            SyncActionResult.STATUS_LOCKED -> store.setPendingStatus(sent.editedAt, PendingStatus.Locked, result.message)
            SyncActionResult.STATUS_CONFLICT -> conflictFrom(sent, result, pc)
            // Arrived too late: it goes again with a new id (the edit is still waiting).
            SyncActionResult.STATUS_LATE -> Unit
            null, SyncActionResult.STATUS_UNSUPPORTED -> store.setPendingStatus(
                sent.editedAt, PendingStatus.NeedsPcUpdate, "Update AccountabiliBuddy on $name to change the plan from here.",
            )
            else -> store.setPendingStatus(sent.editedAt, PendingStatus.Invalid, result.message ?: "$name refused this plan.")
        }
    }

    private fun trySend() {
        if (_conflict.value != null) {
            return
        }

        val pending = store.state.value.pendingPlan ?: return
        val pc = remote.target() ?: return
        if (actions.pending.value.any { it.kind == SyncAction.DAY_PLAN_EDIT }) {
            return
        }

        // A PC too old to answer isn't asked; the edit keeps waiting (the card shows the update note) and goes once it updates.
        if (!PcFileControl.supportsControl(pc.file.device)) {
            return
        }

        val ready = when (pending.status) {
            PendingStatus.Waiting -> true
            // Locked while a focus session counts on the PC (its file's running session, breaks aside).
            PendingStatus.Locked -> PcFileControl.liveRunning(pc.file, Instant.now())?.isBreak ?: true
            // NeedsPcUpdate came from a real answer of an old PC; the PC's file now supports it, so it goes again.
            PendingStatus.NeedsPcUpdate -> true
            PendingStatus.Invalid -> false
        }
        if (ready) {
            actions.add(SyncAction.DAY_PLAN_EDIT, pc.file.device.deviceId, text = Sent.write(pending))
        }
    }

    // The PC's plan from its file (the result names only its version and edit time).
    private fun conflictFrom(sent: Sent, result: SyncActionResult, pc: ReceivedDevice) {
        val pending = store.state.value.pendingPlan?.takeIf { it.editedAt == sent.editedAt } ?: return
        val filePlan = pc.file.dayPlan
        val pcPlan = filePlan?.let { runCatching { ProfileDocumentParser.dayPlan(Json.parseToJsonElement(it.planJson).jsonObject) }.getOrNull() }
        if (filePlan == null || pcPlan == null) {
            // Nothing of the PC's plan to show, so there's nothing to pick between; sending it again would only repeat the conflict.
            store.setPendingStatus(sent.editedAt, PendingStatus.Invalid, "${pc.file.device.name} reported a conflict but didn't share its plan. Try again after it syncs.")
            return
        }

        val pcVersion = result.planVersion ?: filePlan.version
        val pcEditedAt = result.planEditedAt ?: filePlan.editedAt
        store.updatePcPlan(pcPlan, pcVersion, pcEditedAt)
        conflictPcName = pc.file.device.name
        _conflict.value = DayPlanConflict(
            pcPlan = pcPlan,
            pcVersion = pcVersion,
            pcEditedAt = pcEditedAt,
            phonePlan = pending.edited,
            phoneEditedAt = pending.editedAt,
            pcChanges = DayPlanChanges.describe(pending.basePlan, pcPlan),
            phoneChanges = DayPlanChanges.describe(pending.basePlan, pending.edited),
        )
    }

    /** The action's text: { "basedOn", "editedAt", "plan" } (ADR 42), and back. */
    private class Sent(val editedAt: Instant, val plan: DayPlan) {
        companion object {
            fun write(pending: PendingDayPlan): String = buildJsonObject {
                put("basedOn", pending.baseVersion)
                put("editedAt", pending.editedAt.toString())
                put("plan", DayPlanJson.write(pending.edited))
            }.toString()

            fun read(text: String?): Sent? = runCatching {
                val json = Json.parseToJsonElement(text!!).jsonObject
                val editedAt = OffsetDateTime.parse((json["editedAt"] as kotlinx.serialization.json.JsonPrimitive).content).toInstant()
                Sent(editedAt, ProfileDocumentParser.dayPlan(json["plan"] as JsonObject))
            }.getOrNull()
        }
    }
}
