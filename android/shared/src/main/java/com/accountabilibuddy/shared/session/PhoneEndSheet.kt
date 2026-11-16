package com.accountabilibuddy.shared.session

import java.time.Duration
import java.time.Instant

/** What a button of the End sheet does (the sheet maps each to one controller call). */
enum class EndSheetAction {
    /** [PhoneSessionController.endEarly] with no reason: ProductaBuddy and a session that ends on command (free). */
    END_NOW,

    /** "Confirm · ends in 0:42": [PhoneSessionController.endEarly] with the typed reason during the wait; the end is armed (ADR 66 twin). */
    CONFIRM,

    /** "End session" once the wait is over: [PhoneSessionController.endEarly] with the typed reason. */
    END,

    /** R9: [PhoneSessionController.endAtOnce]. */
    ALL_DONE,

    /** [PhoneSessionController.cancelEnd] (free); also what closing the sheet does while an End is in progress without being armed. */
    CANCEL,
}

/** One button of the End sheet. */
data class EndSheetButton(val text: String, val action: EndSheetAction, val enabled: Boolean)

/**
 * What the phone's End sheet shows now (PS3a): built by [PhoneEndSheet.state] from the record, the mode read live (S3), the phone's wait and reason
 * setting, the session time and what is typed. No view logic in the sheet beyond drawing this.
 *
 * @property waitLeftSeconds Whole seconds of the wait still to run (0 when it is over, or there is none); [waitSeconds] is its full length (the ring).
 * @property armedLine Set once the end is confirmed during the wait: it ends by itself ([PhoneSessionRecord.armedEnd]); the sheet then offers Cancel only.
 * @property lmpLine The older LockMyPhone-Extended's lock outlives the session (Q1193); the sheet offers the update next to it.
 */
data class PhoneEndSheetState(
    val title: String,
    val body: String,
    val costFree: Boolean,
    val waitSeconds: Int,
    val waitLeftSeconds: Int,
    val minReason: Int,
    val reasonCount: Int,
    val asksReason: Boolean,
    val reasonEnabled: Boolean,
    val armedLine: String?,
    val primary: EndSheetButton?,
    val allDone: EndSheetButton?,
    val cancel: EndSheetButton,
    val lmpLine: String?,
)

/**
 * R8 and R9 as the End sheet's presenter (PS3a). AccountabiliBuddy, a timed session: a wait counted from when the sheet opened ([PhoneSessionEngine.openEnd]) and
 * a reason of the setting's length; "Confirm" works during the wait (it arms the end, which applies at the wait's end whatever happens to the screen), "End
 * session" once the wait is over; both need the reason; Cancel is free. ProductaBuddy, and a session with no set end: "End now", no wait, no reason. "All tasks
 * done · End now" when [PhoneSessionEndRules.canEndAtOnce] says so. Pure: the sheet only draws, and each button is one engine call.
 */
object PhoneEndSheet {
    /**
     * @param reason What is typed (private: only its length is read here).
     * @param lmpLockUntil When an older LockMyPhone-Extended (no hold) was asked to lock for this session, which only ends at this time; null when
     * that doesn't apply (Q1193 of the PS6a review).
     * @param time The 12-hour time text.
     */
    fun state(
        record: PhoneSessionRecord,
        mode: PhoneSessionMode,
        settings: PhoneEndSettings,
        now: Instant,
        reason: String,
        lmpLockUntil: Instant?,
        time: (Instant) -> String,
    ): PhoneEndSheetState {
        val terms = PhoneSessionEndRules.terms(mode, record, settings)
        val allDoneNow = PhoneSessionEndRules.canEndAtOnce(record, now)
        val lmp = lmpLockUntil?.let { lmpLine(it, time) }
        val cancel = EndSheetButton("Keep going", EndSheetAction.CANCEL, enabled = true)
        val reasonCount = reason.trim().length

        if (terms.isNone) {
            val body = when {
                mode == PhoneSessionMode.PRODUCTA_BUDDY -> "ProductaBuddy ends it at once: no wait, no reason."
                else -> "A session with no set end ends when you end it."
            }
            // PB with every task done: the one button says so (R9 reads the same in both modes).
            val primary = if (allDoneNow) EndSheetButton("All tasks done · End now", EndSheetAction.ALL_DONE, enabled = true)
            else EndSheetButton("End now", EndSheetAction.END_NOW, enabled = true)
            return PhoneEndSheetState("End the session?", body, true, 0, 0, 0, reasonCount, false, false, null, primary, null, cancel, lmp)
        }

        // As the engine reads it ([PhoneSessionEngine.endEarly]): an End opened more than 10 minutes ago, or in the future, counts as a new one.
        val requestedAt = record.endRequestedAt?.takeIf { !it.isAfter(now) && Duration.between(it, now) <= PhoneSessionEngine.END_REQUEST_KEPT_FOR } ?: now
        val readyAt = PhoneSessionEndRules.readyAt(terms, requestedAt)
        val left = secondsLeft(now, readyAt)
        val body = "Ending early takes ${waitText(terms.wait)} and a reason of at least ${terms.minReasonLength} characters."
        val all = if (allDoneNow) EndSheetButton("All tasks done · End now", EndSheetAction.ALL_DONE, enabled = true) else null
        val armed = record.armedEnd
        if (armed != null) {
            val line = "Confirmed. It ends by itself at ${time(armed.readyAt)}, even if you lock the screen. Cancel to keep going."
            return PhoneEndSheetState(
                "Ending at ${time(armed.readyAt)}", body, false, terms.wait.seconds.toInt(), secondsLeft(now, armed.readyAt), terms.minReasonLength,
                reasonCount, true, false, line, null, all, EndSheetButton("Cancel the end", EndSheetAction.CANCEL, enabled = true), lmp,
            )
        }

        val reasonOk = PhoneSessionEndRules.reasonLongEnough(terms, reason)
        val primary = if (left > 0) EndSheetButton("Confirm · ends in ${clock(left)}", EndSheetAction.CONFIRM, enabled = reasonOk)
        else EndSheetButton("End session", EndSheetAction.END, enabled = reasonOk)
        return PhoneEndSheetState(
            "End the session early?", body, false, terms.wait.seconds.toInt(), left, terms.minReasonLength, reasonCount, true, true, null, primary, all, cancel, lmp,
        )
    }

    /** "1:05": minutes and two-digit seconds. */
    fun clock(seconds: Int): String = "${seconds / 60}:${"%02d".format(seconds % 60)}"

    /** "a 1 minute wait", "a 45 second wait", "a 1 minute 30 second wait" (the units are adjectives, so singular). */
    fun waitText(wait: Duration): String {
        val total = wait.seconds.toInt()
        val minutes = total / 60
        val seconds = total % 60
        val parts = buildList {
            if (minutes > 0) add("$minutes minute")
            if (seconds > 0 || minutes == 0) add("$seconds second")
        }
        return "a ${parts.joinToString(" ")} wait"
    }

    /** Q1193: an older LockMyPhone-Extended has no hold the phone can release, so its lock stays until the session's set end. */
    fun lmpLine(until: Instant, time: (Instant) -> String): String = "LockMyPhone-Extended keeps its lock until ${time(until)}."

    private fun secondsLeft(now: Instant, at: Instant): Int = ((Duration.between(now, at).toMillis() + 999) / 1000).toInt().coerceAtLeast(0)
}
