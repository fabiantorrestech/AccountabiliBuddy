package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * A PC's answer to one of the phone's [SyncAction]s (ADR 42), in the PC's file ("actionResults"), as on the PC (Core's
 * SyncActionResult). A PC that writes results writes one for every action it saw, kinds it doesn't know included
 * ([STATUS_UNSUPPORTED]), so the phone tells a refusal from an old PC's silent ack. Kept while the action is still in
 * the phone's file.
 *
 * @property ok Applied (or nothing left to do); false when refused.
 * @property status One of the STATUS_ constants.
 * @property message For the phone to show; null when there's nothing to say.
 * @property planVersion A day plan edit's: the PC's plan version now (on a clash, and after saving).
 * @property planEditedAt A day plan clash's: when the PC's plan last changed, if known.
 */
data class SyncActionResult(
    val id: String,
    val ok: Boolean,
    val status: String,
    val message: String? = null,
    val planVersion: String? = null,
    val planEditedAt: Instant? = null,
) {
    companion object {
        const val STATUS_APPLIED = "applied"
        const val STATUS_CONFLICT = "conflict"
        const val STATUS_LOCKED = "locked"
        const val STATUS_INVALID = "invalid"
        const val STATUS_REFUSED = "refused"
        const val STATUS_LATE = "late"
        const val STATUS_UNSUPPORTED = "unsupported"
    }
}
