package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncState

/**
 * What a device is, for the small icon left of its row. [iconPath] is an outline in a 16 × 16 box (SVG path syntax), the
 * same drawing the PC's Devices tab uses.
 */
enum class DeviceKind(val iconPath: String) {
    PC("M2 3.5h12v8H2z M6 14h4 M8 11.5V14"),
    PHONE("M5.5 1.5h5a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1v-11a1 1 0 0 1 1-1z M7 12.5h2"),
    WATCH("M8 4a4 4 0 1 1 0 8a4 4 0 1 1 0-8z M6 4.6L6.6 1.5h2.8L10 4.6 M6 11.4l0.6 3.1h2.8l0.6-3.1 M8 6.3V8l1 1");

    companion object {
        fun of(cardKind: String): DeviceKind = when (cardKind) {
            SyncDeviceCard.KIND_PC -> PC
            SyncDeviceCard.KIND_WATCH -> WATCH
            else -> PHONE
        }
    }
}

/** The dot's colour on a device row: a sync state, or this device itself. The words beside it say the same, so colour is never alone. */
enum class DeviceTone { UP_TO_DATE, BEHIND, WAITING, UNLINKED, THIS_DEVICE }

/**
 * One device on Devices & sync: its icon, name, a detail line ("AB 3.4.2 · PC · Google Drive"), and its status at the right (a
 * dot and a few words: how long ago, "Now", "Removed"). [indented] rows sit under the device above them (this phone's linked watch).
 *
 * @property status What the right side says: "1 min ago", "Now", "Connected", "Removed".
 * @property stateLabel The sync state in words, for a screen reader ("Behind", "Up to date").
 */
data class DeviceRow(
    val name: String,
    val detail: String,
    val status: String,
    val stateLabel: String,
    val tone: DeviceTone,
    val kind: DeviceKind,
    val indented: Boolean = false,
    /** Set on a device that can be removed from the group (ADR 54): the row gets a ⋯ menu. Not on this phone, its watch, a watch or a removed device. */
    val removableId: java.util.UUID? = null,
)

/** The rows for this phone and its linked watch (no Android here, so the checks can run them). */
object OwnDeviceRows {
    fun tone(state: SyncState): DeviceTone = when (state) {
        SyncState.UP_TO_DATE -> DeviceTone.UP_TO_DATE
        SyncState.BEHIND -> DeviceTone.BEHIND
        SyncState.WAITING -> DeviceTone.WAITING
        SyncState.UNLINKED -> DeviceTone.UNLINKED
    }

    /** "AB 3.4.2" for a version, nothing for a blank one. */
    fun versionPart(appVersion: String): String? = appVersion.takeIf { it.isNotBlank() }?.let { "AB $it" }

    /**
     * This phone first, then its linked watch indented under it: "Connected" while the Data Layer sees it with the app
     * ([detectedIds]), else "Not connected". [appVersion] is this phone's own; [watchVersion] the linked watch's when a file said.
     */
    fun rows(deviceName: String, joined: Boolean, watch: WatchLinkSettings, detectedIds: Set<String>, appVersion: String = "", watchVersion: String? = null): List<DeviceRow> {
        val phone = DeviceRow(
            deviceName,
            listOfNotNull(versionPart(appVersion), if (joined) "this phone · in your device group" else "this phone · not linked to your other devices yet").joinToString(" · "),
            "Now",
            "This phone",
            DeviceTone.THIS_DEVICE,
            DeviceKind.PHONE,
        )
        val nodeId = watch.linkedNodeId ?: return listOf(phone)
        val connected = nodeId in detectedIds
        val watchRow = DeviceRow(
            watch.linkedName ?: "Pixel Watch",
            listOfNotNull(
                watchVersion?.let(::versionPart),
                "Wear OS",
                if (connected) "through this phone" else "linked, not reachable right now",
            ).joinToString(" · "),
            if (connected) "Connected" else "Not connected",
            if (connected) "Connected" else "Not connected",
            if (connected) DeviceTone.UP_TO_DATE else DeviceTone.WAITING,
            DeviceKind.WATCH,
            indented = true,
        )
        return listOf(phone, watchRow)
    }
}
