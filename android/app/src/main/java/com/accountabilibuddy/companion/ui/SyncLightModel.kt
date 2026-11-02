package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncState
import java.time.Duration
import java.time.Instant

/** The sync light's dot (board 20a): green when all is in step, a hollow ring while syncing, lilac when several are behind, red when this device can't sync. */
enum class SyncDot { InSync, Syncing, SeveralBehind, Offline }

/** One device glyph in the light and one row in its popover. A [dimmed] device is behind (or, for the watch, out of reach). */
data class SyncLightDevice(val name: String, val kind: DeviceKind, val dimmed: Boolean, val detail: String)

/** What the sync light draws: the dot, the glyphs, and the popover's headline. */
data class SyncLightModel(val dot: SyncDot, val devices: List<SyncLightDevice>, val headline: String)

/** Works the sync light out from the Devices & sync state (no Android here). */
object SyncLightRules {
    /** A phone's or watch's file is expected about once a day when nothing else says, as on the PC's light. */
    private val SLOW_INTERVAL: Duration = Duration.ofDays(1)

    /** This many other devices older than [SEVERAL_AGE] make the dot lilac. */
    private const val SEVERAL_COUNT = 2
    private val SEVERAL_AGE: Duration = Duration.ofHours(1)

    /**
     * The light's model, or null when there is nothing to show (this phone isn't linked to any device). Hollow ring while
     * [refreshing]; red when a sync method that is on reports a problem; lilac when 2 or more other devices are over an hour
     * old; otherwise green. A device's glyph dims once its file is older than its interval plus 5 minutes.
     */
    fun of(devices: DevicesState, watch: WatchSectionState, detectedWatchIds: Set<String>, refreshing: Boolean, now: Instant): SyncLightModel? {
        if (!devices.joined && devices.received.isEmpty()) return null

        val others = devices.received.map { received ->
            val card = received.file.device
            val age = Duration.between(card.writtenAt, now).coerceAtLeast(Duration.ZERO)
            val interval = if (card.kind == SyncDeviceCard.KIND_PC) DevicesModel.DEFAULT_INTERVAL else SLOW_INTERVAL
            Triple(card, age, SyncState.of(age, interval) == SyncState.BEHIND)
        }
        val linkedWatchName = watch.settings.linkedName.takeIf { watch.settings.linkedNodeId != null }

        val glyphs = buildList {
            add(SyncLightDevice(devices.deviceName, DeviceKind.PHONE, dimmed = false, detail = "now"))
            watch.settings.linkedNodeId?.let { nodeId ->
                val near = nodeId in detectedWatchIds
                add(SyncLightDevice(linkedWatchName ?: "Pixel Watch", DeviceKind.WATCH, dimmed = !near, detail = if (near) "connected" else "not reachable"))
            }
            others.filterNot { (card, _, _) -> card.name == linkedWatchName }.forEach { (card, age, behind) ->
                add(SyncLightDevice(card.name, DeviceKind.of(card.kind), dimmed = behind, detail = DevicesModel.ago(age)))
            }
        }.sortedWith(compareBy({ it.kind.ordinal }, { it.name }))

        val problem = listOfNotNull(
            devices.drive.problem.takeIf { devices.settings.driveOn },
            devices.relay.problem.takeIf { devices.settings.relayOn },
            devices.folder.problem.takeIf { devices.settings.folderOn },
        ).firstOrNull()
        val severalBehind = others.count { (_, age, _) -> age > SEVERAL_AGE } >= SEVERAL_COUNT

        val dot = when {
            problem != null -> SyncDot.Offline
            refreshing -> SyncDot.Syncing
            severalBehind -> SyncDot.SeveralBehind
            else -> SyncDot.InSync
        }
        return SyncLightModel(dot, glyphs, headline(devices, problem, now))
    }

    private fun headline(devices: DevicesState, problem: String?, now: Instant): String {
        if (problem != null) return "This phone can't sync: $problem"
        val methods = listOfNotNull(
            "Google Drive".takeIf { devices.settings.driveOn },
            "own relay".takeIf { devices.settings.relayOn },
            "sync folder".takeIf { devices.settings.folderOn },
        ).ifEmpty { listOf("sync files") }
        val last = listOfNotNull(
            devices.drive.lastSyncAt.takeIf { devices.settings.driveOn },
            devices.relay.lastSyncAt.takeIf { devices.settings.relayOn },
            devices.folder.lastSyncAt.takeIf { devices.settings.folderOn },
            devices.received.maxOfOrNull { it.receivedAt },
        ).maxOrNull() ?: return "Not synced yet"
        val age = Duration.between(last, now).coerceAtLeast(Duration.ZERO)
        return "Synced ${DevicesModel.ago(age)} · ${methods.joinToString(", ")}"
    }
}
