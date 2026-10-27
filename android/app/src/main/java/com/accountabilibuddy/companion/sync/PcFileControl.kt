package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.link.PcProfile
import com.accountabilibuddy.companion.link.PcState
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * The PC this phone controls through the sync files (ADR 42) and what its file says: the newest PC file's PC unless the
 * user picked one in Devices; whether that PC answers control actions (0.1.107 on); and its session as a [PcState], so
 * the PC card and the start picker read the same whether the state came by the link or by a file.
 */
object PcFileControl {
    /** The first PC version that answers start, end early and plan edits in its file. */
    const val CONTROL_VERSION = "0.1.107"

    /** The first PC version that answers noise commands and writes its noise player in its file (ADR 42, 36). */
    const val NOISE_VERSION = "0.1.109"

    // A file older than this doesn't say the session still runs; a phase over for longer than this has ended (the PC went off).
    private val FRESH: Duration = Duration.ofHours(12)
    private val OVER_FOR: Duration = Duration.ofMinutes(10)

    /** The PC files on this phone, newest first. */
    fun pcs(received: List<ReceivedDevice>): List<ReceivedDevice> =
        received.filter { it.file.device.kind == SyncDeviceCard.KIND_PC }.sortedByDescending { it.file.device.writtenAt }

    /** The PC to control: the one picked in Devices ([chosenId], a sync device id) while its file is here, else the newest. */
    fun target(received: List<ReceivedDevice>, chosenId: String?): ReceivedDevice? {
        val pcs = pcs(received)
        return pcs.firstOrNull { chosenId != null && it.file.device.deviceId.toString() == chosenId } ?: pcs.firstOrNull()
    }

    /** True when the PC's app answers control actions (by the version on its card). */
    fun supportsControl(card: SyncDeviceCard): Boolean = compareVersions(card.appVersion, CONTROL_VERSION) >= 0

    /** True when the PC's app answers noise commands (by the version on its card). */
    fun supportsNoise(card: SyncDeviceCard): Boolean = compareVersions(card.appVersion, NOISE_VERSION) >= 0

    /** What to show instead of the controls for a PC too old to answer them; null when it's new enough. */
    fun updateNeeded(card: SyncDeviceCard): String? =
        if (supportsControl(card)) null else "Update AccountabiliBuddy on ${card.name} to start and end its sessions from here."

    /** The PC file's running session while it can still be running: written in the last 12 hours, its phase not over for 10 minutes. */
    fun liveRunning(file: SyncFile, now: Instant): com.accountabilibuddy.shared.sync.SyncRunning? {
        val running = file.running ?: return null
        if (Duration.between(file.device.writtenAt, now) > FRESH) {
            return null
        }

        return running.takeIf { it.phaseEndsAt?.let { end -> now.isBefore(end.plus(OVER_FOR)) } ?: true }
    }

    /** The PC's session as its file says, in the link's shape (a running session in a file always counts as focus). */
    fun stateOf(file: SyncFile, now: Instant): PcState {
        val running = liveRunning(file, now)
        val zone = ZoneId.systemDefault()
        return PcState(
            mode = running?.let { "manual" },
            phase = when {
                running == null -> "idle"
                running.isBreak -> "shortBreak"
                else -> "focus"
            },
            countsAsFocus = running != null,
            profileId = running?.profileId,
            profileName = running?.profile,
            phaseStartedAt = running?.phaseStartedAt?.let { OffsetDateTime.ofInstant(it, zone) },
            phaseEndsAt = running?.phaseEndsAt?.let { OffsetDateTime.ofInstant(it, zone) },
            profiles = file.profileOptions.orEmpty().map { PcProfile(it.id, it.name) },
            countUpProfileId = running?.countUpProfileId ?: file.defaultProfileId,
            tasks = if (running == null) emptyList() else file.sessions.lastOrNull { it.end == null }?.tasks.orEmpty(),
            currentTask = running?.currentTask,
            comebacks = file.comebacks,
        )
    }

    /** Compares dotted versions number by number ("0.1.107" > "0.1.99"); a part that isn't a number counts as 0. */
    fun compareVersions(a: String, b: String): Int {
        val left = a.substringBefore('+').substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 }
        val right = b.substringBefore('+').substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 }
        for (i in 0 until maxOf(left.size, right.size)) {
            val difference = left.getOrElse(i) { 0 } - right.getOrElse(i) { 0 }
            if (difference != 0) {
                return difference
            }
        }

        return 0
    }
}
