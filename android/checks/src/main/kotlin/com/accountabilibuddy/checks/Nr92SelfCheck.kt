package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.sync.NoiseVersions
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncActionResult
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncNoise
import com.accountabilibuddy.shared.sync.SyncNoiseOps
import java.time.Instant
import java.util.UUID

/**
 * ADR 92: a phone's file carries its noise player, the play and pause commands it sends or answers, and its acks and answers, all
 * read back as written (the shape the PC reads: the same "noise", "actions", "acks" and "actionResults" as a PC's file); and play
 * and pause go only to apps new enough to answer them.
 */
object Nr92SelfCheck {
    private fun id(n: Int): UUID = UUID.fromString("00000000-0000-0000-0000-%012d".format(n))

    fun run(report: CheckReport) {
        val at = Instant.parse("2026-11-16T18:00:00Z")
        val pc = id(2)
        val card = SyncDeviceCard(id(1), "Pixel", SyncDeviceCard.KIND_PHONE, "0.1.82", at, listOf(SyncDeviceCard.METHOD_DRIVE), false, emptyMap())
        val pause = SyncAction(id(10).toString(), SyncAction.NOISE, pc, text = """{"op":"${SyncNoiseOps.PAUSE}"}""", at = at)
        val answered = id(11).toString()
        val file = SyncFile(
            SyncFile.CURRENT_FORMAT_VERSION, card, emptyList(), emptyList(), null, emptyList(),
            actions = listOf(pause),
            acks = listOf(answered),
            actionResults = listOf(SyncActionResult(answered, false, SyncActionResult.STATUS_REFUSED, "Connect headphones on Pixel")),
            noise = SyncNoise(true, "Rain", 0.4, false, at),
        )

        val back = SyncFileJson.read(SyncFileJson.write(file))
        report.check("noise-remote: a phone's file reads back", back != null)
        report.check("noise-remote: the phone's noise player reads back", back?.noise == file.noise, "${back?.noise}")
        report.check("noise-remote: a pause command reads back", back?.actions == file.actions, "${back?.actions}")
        report.check("noise-remote: acks and answers read back", back?.acks == file.acks && back?.actionResults == file.actionResults, "${back?.acks} ${back?.actionResults}")

        fun cardOf(kind: String, version: String) = card.copy(kind = kind, appVersion = version)
        report.check("noise-remote: play/pause not sent to a PC before 0.1.147", !NoiseVersions.supportsPlayPause(cardOf(SyncDeviceCard.KIND_PC, "0.1.146")))
        report.check("noise-remote: play/pause sent to a PC at 0.1.147", NoiseVersions.supportsPlayPause(cardOf(SyncDeviceCard.KIND_PC, "0.1.147")))
        report.check("noise-remote: play/pause not sent to a phone before 0.1.82", !NoiseVersions.supportsPlayPause(cardOf(SyncDeviceCard.KIND_PHONE, "0.1.81")))
        report.check("noise-remote: play/pause sent to a phone at 0.1.82", NoiseVersions.supportsPlayPause(cardOf(SyncDeviceCard.KIND_PHONE, "0.1.82")))
    }
}
