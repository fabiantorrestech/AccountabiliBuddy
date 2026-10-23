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

/** The pill's colour: a sync state, or this device itself. */
enum class PillTone { UP_TO_DATE, BEHIND, WAITING, UNLINKED, THIS_DEVICE }

/**
 * One device on the Devices tab: its icon, name, detail and pill. [indented] rows sit under the device above them (this
 * phone's linked watch).
 */
data class DeviceRow(
    val name: String,
    val detail: String,
    val pillText: String,
    val pillTone: PillTone,
    val kind: DeviceKind,
    val indented: Boolean = false,
)

/** The rows for this phone and its linked watch (no Android here, so the checks can run them). */
object OwnDeviceRows {
    fun tone(state: SyncState): PillTone = when (state) {
        SyncState.UP_TO_DATE -> PillTone.UP_TO_DATE
        SyncState.BEHIND -> PillTone.BEHIND
        SyncState.WAITING -> PillTone.WAITING
        SyncState.UNLINKED -> PillTone.UNLINKED
    }

    /**
     * This phone first, then its linked watch indented under it: "Connected" while the Data Layer sees it with the app
     * ([detectedIds]), else "Not connected".
     */
    fun rows(deviceName: String, joined: Boolean, watch: WatchLinkSettings, detectedIds: Set<String>): List<DeviceRow> {
        val phone = DeviceRow(
            deviceName,
            if (joined) "This phone · in your device group" else "This phone · not linked to your other devices yet",
            "This phone",
            PillTone.THIS_DEVICE,
            DeviceKind.PHONE,
        )
        val nodeId = watch.linkedNodeId ?: return listOf(phone)
        val connected = nodeId in detectedIds
        val watchRow = DeviceRow(
            watch.linkedName ?: "Pixel Watch",
            if (connected) "Watch · linked to this phone" else "Watch · linked, not reachable right now",
            if (connected) "Connected" else "Not connected",
            if (connected) PillTone.UP_TO_DATE else PillTone.WAITING,
            DeviceKind.WATCH,
            indented = true,
        )
        return listOf(phone, watchRow)
    }
}
