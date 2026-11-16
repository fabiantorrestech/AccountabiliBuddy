package com.accountabilibuddy.companion.session

import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.session.PhoneSessionSyncInput
import com.accountabilibuddy.shared.session.PhoneSessionSyncWriter
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import java.util.UUID

/**
 * This phone's own phone sessions as a device of the group (ADR 85, PS2b), so everything that counts focus counts them the way it counts a PC's or
 * another phone's: through [com.accountabilibuddy.shared.sync.GroupSessions.openPieceEnd] and the union of every device's windows, which counts a
 * minute two devices both ran once (R4). Pure; the checks drive it.
 *
 * Counting readers use [focusFiles]; device lists, receipts, removal and the other device-only readers keep to the group's received files
 * ([com.accountabilibuddy.companion.sync.SyncCoordinator.allReceived]), because this phone is not a device it received a file from.
 */
object PhoneSessionFiles {
    /** The method name of this phone's own part (never a way a file travels). */
    const val OWN_METHOD = "own"

    /**
     * The phone's own sessions as a received-looking device, stamped with the session clock (so the open piece of a running session ends at the session's
     * time now, never at a clock the user can move); null when the phone holds no session. A session whose times are not corrected yet still counts here
     * (it is this phone's own data), though the sync file leaves it out (V-N1).
     */
    fun ownDevice(input: PhoneSessionSyncInput, deviceId: UUID, name: String, appVersion: String): ReceivedDevice? {
        val part = PhoneSessionSyncWriter.forCounting(input) ?: return null
        val card = SyncDeviceCard(deviceId, name, SyncDeviceCard.KIND_PHONE, appVersion, part.writtenAt, emptyList(), false, emptyMap())
        val file = SyncFile(SyncFile.CURRENT_FORMAT_VERSION, card, part.sessions, emptyList(), null, emptyList(), coversFrom = part.coversFrom, running = part.running)
        return ReceivedDevice(file, part.writtenAt, OWN_METHOD, ByteArray(0))
    }

    /** [received] (every device held) and this phone's own sessions: the list every focus-counting reader takes. [own] null leaves it as it is. */
    fun focusFiles(received: List<ReceivedDevice>, own: ReceivedDevice?): List<ReceivedDevice> =
        if (own == null) received else received.filter { it.file.device.deviceId != own.file.device.deviceId } + own
}
