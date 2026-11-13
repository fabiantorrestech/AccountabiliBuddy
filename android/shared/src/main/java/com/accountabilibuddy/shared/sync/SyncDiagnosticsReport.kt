package com.accountabilibuddy.shared.sync

import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.util.UUID

/**
 * The diagnostics report (SYNC1c; the PC's `SyncDiagnosticsExport`): the confirmation board, the unreadable files, the versions and the sync log
 * as one text file for the user to keep or send by hand. It is never sent anywhere automatically. Like the log it can't hold private text:
 * devices appear as "PC 1" and "Phone 1" with their kind and app version, files by their reason and method only (never a name, path, address or
 * id), and nothing from the user's own data. A pure builder; the share sheet is the caller's.
 */
object SyncDiagnosticsReport {
    /**
     * @param selfVersion This phone's app version.
     * @param unreadable The unreadable and leftover files (all, including ignored ones, marked as such).
     * @param ordinals Each device's ordinal ([SyncBoardBuilder.ordinals]).
     * @param logLines The sync log's lines.
     */
    fun build(
        now: OffsetDateTime,
        zone: ZoneId,
        selfVersion: String,
        board: SyncBoard,
        unreadable: List<SyncUnreadableFile>,
        ordinals: Map<UUID, String>,
        logLines: List<String>,
    ): String {
        val text = StringBuilder()
        text.appendLine("AccountabiliBuddy sync diagnostics")
        text.appendLine("Made ${SyncBoardWords.clock(now, now, zone)} (this phone's local time, ${now.withOffsetSameInstant(ZoneOffset.UTC).toLocalDate()})")
        text.appendLine("This phone: app $selfVersion")
        text.appendLine("No names, text, addresses or file names are in this report: devices are numbered by kind.")
        text.appendLine()
        text.appendLine("== Confirmation board ==")
        if (board.notSyncing) text.appendLine("Not syncing: no sync method is on.")
        for (method in board.methods) {
            text.appendLine(method.title)
            if (method.devices.isEmpty()) text.appendLine("  No other devices yet.")
            for (device in method.devices) {
                text.append("  ${device.ordinal} (${device.kind}, app ${device.appVersion}): ${device.dot.color} - ${SyncBoardWords.words(device.dot)}")
                device.confirmedFile?.let { text.append("; confirmed this phone's file from ${SyncBoardWords.clock(it, now, zone)}") }
                device.lastFileAt?.let { text.append("; its newest file ${SyncBoardWords.clock(it, now, zone)}") }
                text.appendLine()
                for (part in device.parts) {
                    text.appendLine("      ${SyncBoardWords.partName(part.code)}: ${part.dot.color} - ${SyncBoardWords.words(part.dot)}")
                }
            }
        }

        text.appendLine()
        text.appendLine("== Files that aren't taken in ==")
        if (unreadable.isEmpty()) text.appendLine("None.")
        for (file in unreadable) {
            val who = file.device?.let { ordinals[it] } ?: if (file.ownStale) "this phone" else "a device"
            text.appendLine(
                "${SyncBoardBuilder.titleOf(file.method)}; $who; ${SyncBoardWords.clock(file.at, now, zone)}; ${SyncBoardWords.reasonOfFile(file.reason)}" +
                    if (file.ignored) "; ignored" else "",
            )
        }

        text.appendLine()
        text.appendLine("== Sync log (last 7 days, UTC) ==")
        if (logLines.isEmpty()) text.appendLine("Empty.")
        for (line in logLines) text.appendLine(line)
        return text.toString()
    }
}
