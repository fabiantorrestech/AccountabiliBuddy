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

    /** This many other devices more than [SEVERAL_EXTRA] past their expected interval make the dot lilac. */
    private const val SEVERAL_COUNT = 2
    private val SEVERAL_EXTRA: Duration = Duration.ofHours(1)

    /**
     * The light's model, or null when there is nothing to show (this phone isn't linked to any device). Hollow ring while
     * [refreshing]; red only when every live sync method that is on (Drive, the relay, the sync folder) reports a problem, so
     * nothing leaves this phone (one working method keeps it green, and the popover names the other); lilac when 2 or more other
     * devices are over an hour later than their file is due; otherwise green. A device's glyph dims once its file is older than its
     * interval plus 5 minutes; when this phone can't sync, every other glyph dims.
     */
    fun of(devices: DevicesState, watch: WatchSectionState, detectedWatchIds: Set<String>, refreshing: Boolean, now: Instant): SyncLightModel? {
        if (!devices.joined && devices.received.isEmpty()) return null

        // Per device: its card, its age, whether it is behind, and whether it is far behind (an hour past its due time).
        val others = devices.received.map { received ->
            val card = received.file.device
            val age = Duration.between(card.writtenAt, now).coerceAtLeast(Duration.ZERO)
            val interval = if (card.kind == SyncDeviceCard.KIND_PC) DevicesModel.DEFAULT_INTERVAL else SLOW_INTERVAL
            Quad(card, age, SyncState.of(age, interval) == SyncState.BEHIND, age > interval + SEVERAL_EXTRA)
        }
        val linkedWatch = watch.settings.linkedNodeId != null
        val linkedWatchName = watch.settings.linkedName.takeIf { linkedWatch }

        val glyphs = buildList {
            add(SyncLightDevice(devices.deviceName, DeviceKind.PHONE, dimmed = false, detail = "now"))
            watch.settings.linkedNodeId?.let { nodeId ->
                val near = nodeId in detectedWatchIds
                add(SyncLightDevice(linkedWatchName ?: "Pixel Watch", DeviceKind.WATCH, dimmed = !near, detail = if (near) "connected" else "not reachable"))
            }
            // The linked watch is drawn once, from the Data Layer above; a watch card in a sync file is that same watch.
            others.filterNot { it.card.kind == SyncDeviceCard.KIND_WATCH && linkedWatch }.forEach { other ->
                add(SyncLightDevice(other.card.name, DeviceKind.of(other.card.kind), dimmed = other.behind, detail = DevicesModel.ago(other.age)))
            }
        }.sortedWith(compareBy({ it.kind.ordinal }, { it.name }))

        // The live methods that are on, and which of them report a problem.
        val methods = listOf(
            Triple("Google Drive", devices.settings.driveOn, devices.drive.problem),
            Triple("own relay", devices.settings.relayOn, devices.relay.problem),
            Triple("sync folder", devices.settings.folderOn, devices.folder.problem),
        ).filter { it.second }
        val failing = methods.filter { it.third != null }
        val problem = failing.firstOrNull()?.third.takeIf { methods.isNotEmpty() && failing.size == methods.size }
        val severalBehind = others.count { it.farBehind } >= SEVERAL_COUNT

        val dot = when {
            problem != null -> SyncDot.Offline
            refreshing -> SyncDot.Syncing
            severalBehind -> SyncDot.SeveralBehind
            else -> SyncDot.InSync
        }
        // Nothing leaves this phone: the other devices show as out of reach too.
        val shown = if (dot == SyncDot.Offline) glyphs.map { if (it.name == devices.deviceName) it else it.copy(dimmed = true) } else glyphs
        return SyncLightModel(dot, shown, headline(devices, problem, failing.map { it.first }, now))
    }

    private fun headline(devices: DevicesState, problem: String?, failing: List<String>, now: Instant): String {
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
        // One method failing while another works: still in sync, and the popover says which needs a look.
        val attention = if (failing.isEmpty()) "" else ". ${failing.joinToString(", ")} needs attention"
        return "Synced ${DevicesModel.ago(age)} · ${methods.joinToString(", ")}$attention"
    }

    private data class Quad(val card: SyncDeviceCard, val age: Duration, val behind: Boolean, val farBehind: Boolean)
}
