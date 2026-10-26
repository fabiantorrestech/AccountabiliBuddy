package com.accountabilibuddy.companion.blockers

import android.content.Context

/** Another blocker installed on this phone, with its version when Android gives one. Status only. */
data class OtherBlocker(val name: String, val version: String?)

/**
 * Finds other blockers installed on this phone (AppBlock, StayFocused, Freedom) by package name, one
 * PackageManager lookup each; asked when Settings is shown, never on a timer. AccountabiliBuddy doesn't read
 * or change anything of theirs. Their packages are listed in the manifest's `<queries>` (Android 11+).
 */
object OtherBlockers {
    private val known = listOf(
        "AppBlock" to "cz.mobilesoft.appblock",
        "StayFocused" to "com.stayfocused",
        "Freedom" to "to.freedom.android2",
    )

    fun detect(context: Context): List<OtherBlocker> = known.mapNotNull { (name, packageName) ->
        runCatching { context.packageManager.getPackageInfo(packageName, 0) }.getOrNull()
            ?.let { OtherBlocker(name, it.versionName) }
    }

    /** E.g. "Detected: AppBlock 6.2.1 — not controlled by AccountabiliBuddy". */
    fun line(blocker: OtherBlocker): String {
        val name = if (blocker.version.isNullOrBlank()) blocker.name else "${blocker.name} ${blocker.version}"
        return "Detected: $name — not controlled by AccountabiliBuddy"
    }
}
