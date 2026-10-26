package com.accountabilibuddy.companion.model

import java.time.LocalTime

/**
 * One block on one device (ADR 37): [device] "phone" or "pc:<id>", [kind] one of the constants, the blocker's own
 * [id] (a Curbox group id, a Cold Turkey block name) and its name. A port of the PC's Core BlockRef.
 */
data class BlockRef(val device: String, val kind: String, val id: String, val name: String) {
    fun same(other: BlockRef): Boolean = device == other.device && kind == other.kind && id == other.id

    companion object {
        const val PHONE = "phone"
        const val CURBOX_APP_GROUP = "curbox.appGroup"
        const val CURBOX_KEYWORD_GROUP = "curbox.keywordGroup"
        const val CURBOX_GRAYSCALE_GROUP = "curbox.grayscaleGroup"
        const val CURBOX_KEYWORD_BLOCKER = "curbox.keywordBlocker"
        const val CURBOX_REEL_BLOCKER = "curbox.reelBlocker"
        const val COLD_TURKEY_BLOCK = "coldturkey.block"
    }
}

/** A window in which some blocks may be off: [ALLOW] lets the user turn them off, [AUTO] turns them off at [start]; back on at [end]. */
data class BlockLift(val start: LocalTime, val end: LocalTime, val mode: String, val blocks: List<BlockRef>) {
    companion object {
        const val ALLOW = "allow"
        const val AUTO = "auto"
    }
}

/**
 * A day's block rules (schema v10, ADR 37): the blocks it keeps on ([guarded]; null = every block the device saw on),
 * when anything guarded that's off comes back on ([reenableAt], midnight = the end of the day), the heads-up before it
 * ([warnMinutes]; null = none) and the lift windows.
 */
data class BlockRules(
    val guarded: List<BlockRef>?,
    val reenableAt: LocalTime,
    val warnMinutes: Int?,
    val lifts: List<BlockLift>,
    /** Schema v11: when each device's blocks come back on (others use [reenableAt]). */
    val deviceTimes: List<DeviceReenable>? = null,
    /** Schema v11: blocks with their own time, left out of their device's. */
    val blockTimes: List<BlockReenable>? = null,
) {
    fun reenableFor(device: String): LocalTime = deviceTimes?.firstOrNull { it.device == device }?.at ?: reenableAt

    companion object {
        val Default = BlockRules(null, LocalTime.MIDNIGHT, 5, emptyList())
    }
}

/** When one device's blocks come back on that day (schema v11). */
data class DeviceReenable(val device: String, val at: LocalTime)

/** A block that comes back on at its own time that day instead of its device's (schema v11). */
data class BlockReenable(val block: BlockRef, val at: LocalTime)
