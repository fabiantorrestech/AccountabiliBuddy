package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncComeback
import java.time.Instant

/** The PC's open come-backs the phone knows (ADR 30), and which PC they're from; shown read-only on Today. */
data class OpenComebacks(val source: String, val items: List<SyncComeback>) {
    companion object {
        /** The newest received file that carries a list, when it isn't empty. */
        fun of(received: List<ReceivedDevice>, now: Instant): OpenComebacks? =
            received
                .filter { it.file.comebacks != null && !it.file.device.writtenAt.isAfter(now.plusSeconds(60)) }
                .maxByOrNull { it.file.device.writtenAt }
                ?.let { device -> device.file.comebacks?.takeIf { it.isNotEmpty() }?.let { OpenComebacks(device.file.device.name, it) } }
    }
}
