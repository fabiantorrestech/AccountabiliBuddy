package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncParked
import java.time.Instant

/**
 * The open come-backs on Today (ADR 30, 48): the user's parked thoughts from the list every device shares. Ticking one
 * or parking a new one changes that list here and reaches the other devices with the next sync.
 *
 * Next to a PC that predates the shared list (its newest file has no "parked", ADR 48) the list is that PC's open
 * come-backs as before (ADR 30): [legacySource] is its name, and ticking or parking goes to it as sync actions (ADR 26).
 * The card then says to update the PC.
 */
data class OpenComebacks(val items: List<Row>, val legacySource: String? = null) {
    /** One come-back: its id and text. */
    data class Row(val id: String, val text: String)

    companion object {
        private const val CLOCK_SLACK_SECONDS = 60L

        /**
         * The not yet ticked ones of [parked] (live items only), in list order; or, when the newest PC file in [received]
         * has no shared come-back list, that file's open come-backs.
         */
        fun of(parked: List<SyncParked>, received: List<ReceivedDevice>, now: Instant): OpenComebacks {
            val newestPc = received
                .filter { it.file.device.kind == SyncDeviceCard.KIND_PC && !it.file.device.writtenAt.isAfter(now.plusSeconds(CLOCK_SLACK_SECONDS)) }
                .maxByOrNull { it.file.device.writtenAt }
            if (newestPc != null && !newestPc.hasSharedLists) {
                return OpenComebacks(newestPc.file.comebacks.orEmpty().map { Row(it.id, it.text) }, newestPc.file.device.name)
            }

            return OpenComebacks(parked.filter { it.deletedAt == null && it.doneAt == null }.map { Row(it.id, it.text) })
        }
    }
}
