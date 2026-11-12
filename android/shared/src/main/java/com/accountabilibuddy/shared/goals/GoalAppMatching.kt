package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * Which usage rows a screen-time source's entries count, per kind of device. Twin of Core's `GoalAppMatching`. Pure: the caller
 * reads the rows and decides which devices count ([countsOn]).
 */
object GoalAppMatching {
    private const val EXE_SUFFIX = ".exe"
    private const val WWW_PREFIX = "www."

    fun matches(match: GoalAppMatch, row: GoalAppRow, platform: GoalAppPlatform): Boolean {
        val phone = platform == GoalAppPlatform.Phone
        return when (match.kind) {
            GoalAppMatchKind.Package -> phone && row.id == match.text
            GoalAppMatchKind.Process -> !phone && withoutExe(row.id).equals(withoutExe(match.text), ignoreCase = true)
            GoalAppMatchKind.Website -> {
                val rowSite = row.site
                if (phone || rowSite == null) {
                    false
                } else {
                    val site = withoutWww(rowSite)
                    val domain = withoutWww(match.text)
                    domain.isNotEmpty() && (site.equals(domain, ignoreCase = true) || site.endsWith(".$domain", ignoreCase = true))
                }
            }
            GoalAppMatchKind.Name ->
                (row.label != null && row.label.equals(match.text, ignoreCase = true)) ||
                    (!phone && withoutExe(row.id).equals(match.text, ignoreCase = true))
        }
    }

    /** The minutes of [rows] that any entry of [rule] matches; a row counts once however many entries hit it. */
    fun minutesFor(rule: GoalSourceRule, rows: List<GoalAppRow>, platform: GoalAppPlatform): Double {
        val apps = rule.apps?.takeIf { it.isNotEmpty() } ?: return 0.0
        return rows.filter { row -> apps.any { matches(it, row, platform) } }.sumOf { it.minutes }
    }

    /**
     * Whether [deviceId] may count [rule]: every device when [GoalSourceRule.allDevices], else the listed devices, else (none
     * listed) only the device that created it ([GoalSourceRule.createdOn]; a rule written before devices were recorded counts
     * wherever it is read).
     */
    fun countsOn(rule: GoalSourceRule, deviceId: UUID): Boolean {
        if (rule.allDevices) return true
        rule.devices?.let { return deviceId in it }
        return rule.createdOn == null || rule.createdOn == deviceId
    }

    private fun withoutExe(text: String) = if (text.endsWith(EXE_SUFFIX, ignoreCase = true)) text.dropLast(EXE_SUFFIX.length) else text

    private fun withoutWww(text: String) = if (text.startsWith(WWW_PREFIX, ignoreCase = true)) text.drop(WWW_PREFIX.length) else text
}
