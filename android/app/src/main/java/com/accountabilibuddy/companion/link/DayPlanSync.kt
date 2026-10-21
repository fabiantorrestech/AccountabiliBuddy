package com.accountabilibuddy.companion.link

import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.data.DayPlanJson
import com.accountabilibuddy.companion.engine.DayPlanChanges
import com.accountabilibuddy.companion.model.DayPlanConflict
import com.accountabilibuddy.companion.model.PendingDayPlan
import com.accountabilibuddy.companion.model.PendingStatus
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * Sends a day plan edited on the phone to the paired PC (ADR 19) whenever it can: when the link connects, when the PC
 * sends new state or settings, and, after "locked", once the PC's focus session is over. Then it acts on the answer:
 * applied clears the edit, locked and invalid keep it with a note, and a conflict waits for the user's pick.
 */
class DayPlanSync(
    private val store: CompanionStore,
    private val link: LinkClient,
    private val scope: CoroutineScope,
) {
    private val sending = Mutex()
    private val _conflict = MutableStateFlow<DayPlanConflict?>(null)

    /** Both the PC and the phone changed the plan; null when there's nothing to decide. */
    val conflict: StateFlow<DayPlanConflict?> = _conflict.asStateFlow()

    fun start() {
        scope.launch {
            combine(link.status, store.state) { status, state -> status to state.pendingPlan }.collect { (status, pending) ->
                if (pending != null && status is LinkStatus.Connected && _conflict.value == null && readyToSend(pending, status.state)) {
                    send(pending)
                }
            }
        }
    }

    /** "Use this phone's": it goes again on top of the PC's current plan (rebased before the dialog closes). */
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

    private fun readyToSend(pending: PendingDayPlan, pc: PcState?): Boolean = when (pending.status) {
        PendingStatus.Waiting -> true
        // Locked on the PC while a focus session it counts runs; any other state is worth a try.
        PendingStatus.Locked -> pc != null && !(pc.isActive && pc.countsAsFocus && !pc.isBreak)
        PendingStatus.Invalid, PendingStatus.NeedsPcUpdate -> false
    }

    private suspend fun send(pending: PendingDayPlan) {
        if (!sending.tryLock()) {
            return
        }

        try {
            val answer = link.sendDayPlanEdit(pending.baseVersion, DayPlanJson.write(pending.edited), pending.editedAt) ?: return
            // The store checks that the edit is still the one sent; a newer one goes next, built on what the PC took.
            when (answer.status) {
                DayPlanAnswer.APPLIED -> store.finishPendingPlan(pending.editedAt, pending.edited, answer.currentVersion)
                DayPlanAnswer.LOCKED -> store.setPendingStatus(pending.editedAt, PendingStatus.Locked, answer.message)
                DayPlanAnswer.CONFLICT -> if (store.state.value.pendingPlan?.editedAt == pending.editedAt) conflictFrom(pending, answer)
                DayPlanAnswer.UNSUPPORTED -> store.setPendingStatus(
                    pending.editedAt, PendingStatus.NeedsPcUpdate, "Update AccountabiliBuddy on the PC to change the plan from here.",
                )
                else -> store.setPendingStatus(pending.editedAt, PendingStatus.Invalid, answer.message.ifBlank { "The PC refused this plan." })
            }
        } finally {
            sending.unlock()
        }
    }

    private fun conflictFrom(pending: PendingDayPlan, answer: DayPlanAnswer) {
        val pcPlan = answer.currentPlan
        val pcVersion = answer.currentVersion
        if (pcPlan == null || pcVersion == null) {
            // Nothing to compare with; it goes again on the next connection.
            return
        }

        store.updatePcPlan(pcPlan, pcVersion, answer.currentEditedAt)
        _conflict.value = DayPlanConflict(
            pcPlan = pcPlan,
            pcVersion = pcVersion,
            pcEditedAt = answer.currentEditedAt,
            phonePlan = pending.edited,
            phoneEditedAt = pending.editedAt,
            pcChanges = DayPlanChanges.describe(pending.basePlan, pcPlan),
            phoneChanges = DayPlanChanges.describe(pending.basePlan, pending.edited),
        )
    }
}
