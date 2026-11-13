package com.accountabilibuddy.shared.goals

import java.security.MessageDigest
import java.util.UUID

/**
 * The opaque names of the goal history files (ADR 79 shared history, GS3b; the PC's `GoalHistoryNames`): 12 lower-case hex characters of
 * a SHA-256 over the group id and the file's key (kind, device, year, month), the same on Drive and the relay apart from the extension.
 * Every device computes the same name, so a device finds a file by the names of the devices and years it knows.
 */
object GoalHistoryNames {
    /** The first year a history file can be named for (the goals arrived in 2026). */
    const val FIRST_YEAR = 2025

    /** Drive keeps the history files under their own extension next to the sync files. */
    const val DRIVE_EXTENSION = ".abgh"

    /** The relay accepts only a sync file's shape, so the history files use it too (the header tells them apart). */
    const val RELAY_EXTENSION = ".absync"

    fun nameFor(groupId: String, key: GoalHistoryFileKey, extension: String): String {
        val hash = MessageDigest.getInstance("SHA-256").digest(("goalhist|" + groupId + "|" + keyText(key)).toByteArray(Charsets.UTF_8))
        return hash.take(6).joinToString("") { "%02x".format(it) } + extension
    }

    /** The key as text, for state and names: `m|device|yyyy|mm` or `y|rolledBy|yyyy`. */
    fun keyText(key: GoalHistoryFileKey): String {
        val device = (key.deviceId ?: UUID(0L, 0L)).toString()
        return if (key.kind == GoalHistoryFileKind.Month) "m|$device|${pad(key.year, 4)}|${pad(key.month ?: 0, 2)}" else "y|$device|${pad(key.year, 4)}"
    }

    fun parseKey(text: String): GoalHistoryFileKey? {
        val parts = text.split('|')
        val device = parts.getOrNull(1)?.let { runCatching { UUID.fromString(it) }.getOrNull() } ?: return null
        if (parts.size == 4 && parts[0] == "m") {
            val year = parts[2].toIntOrNull() ?: return null
            val month = parts[3].toIntOrNull() ?: return null
            return GoalHistoryFileKey.forMonth(device, year, month)
        }

        if (parts.size == 3 && parts[0] == "y") {
            val year = parts[2].toIntOrNull() ?: return null
            return GoalHistoryFileKey.forYear(year, device)
        }

        return null
    }

    /** The text a device's own month is kept under in the state. */
    fun monthText(year: Int, month: Int): String = "${pad(year, 4)}-${pad(month, 2)}"

    /** Every file name the group's files can have: each device's twelve months and each device's year file, from [FIRST_YEAR] to [lastYear]. */
    fun candidates(groupId: String, extension: String, devices: Iterable<UUID>, lastYear: Int): Map<String, GoalHistoryFileKey> {
        val names = HashMap<String, GoalHistoryFileKey>()
        for (device in devices.distinct()) {
            for (year in FIRST_YEAR..lastYear) {
                val yearKey = GoalHistoryFileKey.forYear(year, device)
                names[nameFor(groupId, yearKey, extension)] = yearKey
                for (month in 1..12) {
                    val monthKey = GoalHistoryFileKey.forMonth(device, year, month)
                    names[nameFor(groupId, monthKey, extension)] = monthKey
                }
            }
        }

        return names
    }

    /** Whether sealed bytes are a goal history file (a month's or a year's header): a sync reader that finds one skips it. */
    fun isHistoryFile(sealedBytes: ByteArray): Boolean =
        sealedBytes.size >= 4 && (startsWith(sealedBytes, "ABM1") || startsWith(sealedBytes, "ABY1"))

    private fun startsWith(bytes: ByteArray, magic: String) = magic.indices.all { bytes[it] == magic[it].code.toByte() }

    private fun pad(value: Int, width: Int) = value.toString().padStart(width, '0')
}
