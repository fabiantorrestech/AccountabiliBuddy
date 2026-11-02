package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.shared.sync.FocusDayCombine
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncState
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/** Devices & sync's rows, stale note and totals line, from what the phone has (no Android here). */
object DevicesModel {
    /** How often another device's file should come when nothing else says (the PC's default interval). */
    val DEFAULT_INTERVAL: Duration = Duration.ofMinutes(15)

    /** A phone's or watch's file is expected about once a day when nothing else says, as on the PC's light. */
    private val SLOW_INTERVAL: Duration = Duration.ofDays(1)

    /** How often a device of [kind] should write, the same for its row, the stale note and the sync light. */
    fun intervalFor(kind: String): Duration = if (kind == SyncDeviceCard.KIND_PC) DEFAULT_INTERVAL else SLOW_INTERVAL

    /** This phone and its watch, then the other devices, then the removed ones whose data is kept. */
    fun allRows(devices: DevicesState, watchDetectedIds: Set<String>, watch: com.accountabilibuddy.shared.WatchLinkSettings, now: Instant): List<DeviceRow> {
        val watchVersion = devices.received.firstOrNull { it.file.device.kind == SyncDeviceCard.KIND_WATCH }?.file?.device?.appVersion
        // The linked watch is drawn once, from the Data Layer under this phone; a watch card in a sync file is that same watch (as in the sync light).
        return OwnDeviceRows.rows(devices.deviceName, devices.joined, watch, watchDetectedIds, devices.appVersion, watchVersion) +
            rows(devices, now, hideWatchCards = watch.linkedNodeId != null)
    }

    fun rows(devices: DevicesState, now: Instant, hideWatchCards: Boolean = false): List<DeviceRow> = liveRows(devices, now, hideWatchCards) + removedRows(devices)

    // The devices still in the group. Each can be removed (a ⋯ menu), except a watch row: those stay as they are.
    private fun liveRows(devices: DevicesState, now: Instant, hideWatchCards: Boolean): List<DeviceRow> = devices.received
        .filterNot { hideWatchCards && it.file.device.kind == SyncDeviceCard.KIND_WATCH }
        .sortedByDescending { it.file.device.writtenAt }
        .map { device ->
            val card = device.file.device
            val age = Duration.between(card.writtenAt, now).coerceAtLeast(Duration.ZERO)
            val state = SyncState.of(age, intervalFor(card.kind))
            DeviceRow(
                card.name,
                listOfNotNull(OwnDeviceRows.versionPart(card.appVersion), kindName(card.kind), methodName(device.method)).joinToString(" · "),
                ago(age),
                state.label,
                OwnDeviceRows.tone(state),
                DeviceKind.of(card.kind),
                removableId = card.deviceId.takeIf { devices.joined && card.kind != SyncDeviceCard.KIND_WATCH },
            )
        }

    // A removed device whose data was kept (ADR 54, 55): "<name> (removed)", no menu, nothing more expected from it.
    private fun removedRows(devices: DevicesState): List<DeviceRow> =
        devices.removed.mapNotNull { removed ->
            val card = devices.allReceived.firstOrNull { it.file.device.deviceId == removed.deviceId }?.file?.device ?: return@mapNotNull null
            DeviceRow(
                "${removed.name} (removed)",
                listOfNotNull(
                    OwnDeviceRows.versionPart(card.appVersion), kindName(card.kind),
                    "removed ${REMOVED_ON.format(removed.removedAt.atZone(ZoneId.systemDefault()))}", "its data is kept",
                ).joinToString(" · "),
                "Removed",
                "Removed",
                DeviceTone.UNLINKED,
                DeviceKind.of(card.kind),
            )
        }

    /**
     * The one line under the list when a device is behind: the one that has been quiet longest, since when, and that it catches up by
     * itself ("Galaxy Tab hasn't synced since 10:35 AM. It catches up next time it's opened."). A count of the others when there are
     * more. Null when none is behind. The time is 12-hour whatever the system says; another day adds its date.
     */
    fun staleNote(devices: DevicesState, now: Instant, zone: ZoneId, hideWatchCards: Boolean = false): StaleNote? {
        val behind = devices.received
            .filterNot { hideWatchCards && it.file.device.kind == SyncDeviceCard.KIND_WATCH }
            .filter { SyncState.of(Duration.between(it.file.device.writtenAt, now).coerceAtLeast(Duration.ZERO), intervalFor(it.file.device.kind)) == SyncState.BEHIND }
        val oldest = behind.minByOrNull { it.file.device.writtenAt }?.file?.device ?: return null
        val at = oldest.writtenAt.atZone(zone)
        val today = now.atZone(zone).toLocalDate()
        val time = (if (at.toLocalDate() == today) CLOCK else CLOCK_WITH_DATE).format(at)
        val more = behind.size - 1
        return StaleNote(oldest.name, " hasn't synced since $time. It catches up next time it's opened." + if (more > 0) " $more more are behind too." else "")
    }

    /** "4:05 PM": a time of day in 12-hour, whatever the system says. */
    fun clockText(at: Instant, zone: ZoneId): String = CLOCK.format(at.atZone(zone))

    private fun kindName(kind: String): String = when (kind) {
        SyncDeviceCard.KIND_PC -> "PC"
        SyncDeviceCard.KIND_WATCH -> "Watch"
        else -> "Phone"
    }

    // E.g. "Oct 28"; a date only, so the 12-hour rule has nothing to apply to.
    private val REMOVED_ON: DateTimeFormatter = DateTimeFormatter.ofPattern("MMM d", Locale.US)
    private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
    private val CLOCK_WITH_DATE: DateTimeFormatter = DateTimeFormatter.ofPattern("MMM d, h:mm a", Locale.US)

    /** E.g. "Today: 5h 10m focused across 3 devices"; null when there's nothing to add up. */
    fun todayLine(devices: DevicesState, now: Instant, zone: ZoneId): String? {
        val today = now.atZone(zone).toLocalDate()
        val combined = FocusDayCombine.combine(devices.allReceived.map { it.file.device.name to it.file }, today, zone, now) ?: return null
        val count = combined.sessions.mapNotNull { it.device }.distinct().size
        val minutes = combined.focusedMinutes
        val time = if (minutes >= 60) "${minutes / 60}h ${"%02d".format(minutes % 60)}m" else "${minutes}m"
        return if (count > 1) "Today: $time focused across $count devices" else "Today: $time focused"
    }

    fun ago(age: Duration): String = when {
        age < Duration.ofMinutes(1) -> "just now"
        age < Duration.ofHours(1) -> "${age.toMinutes()} min ago"
        age < Duration.ofDays(1) -> "${age.toHours()} h ago"
        age < Duration.ofDays(2) -> "1 day ago"
        else -> "${age.toDays()} days ago"
    }

    private fun methodName(method: String): String = when (method) {
        SyncDeviceCard.METHOD_LINK -> "network link (old)"
        SyncDeviceCard.METHOD_FOLDER -> "shared folder"
        SyncDeviceCard.METHOD_QR -> "QR codes"
        SyncDeviceCard.METHOD_DRIVE -> "Google Drive"
        SyncDeviceCard.METHOD_RELAY -> "own relay"
        else -> "sync file"
    }
}
