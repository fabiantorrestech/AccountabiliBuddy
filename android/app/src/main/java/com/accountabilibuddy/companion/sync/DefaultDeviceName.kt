package com.accountabilibuddy.companion.sync

/**
 * The name this phone gives itself in the sync group (CP1a). The system's device name, else the model; the debug build
 * (a separate app, `com.accountabilibuddy.companion.debug`) adds " (debug)" so the group's device list tells it from the
 * release app on the same phone. Pure, so it can be checked without Android.
 */
object DefaultDeviceName {
    const val DEBUG_SUFFIX = " (debug)"
    private const val DEBUG_ID_END = ".debug"

    /** @param systemName Settings.Global.DEVICE_NAME (may be null or blank), @param model Build.MODEL. */
    fun of(systemName: String?, model: String, packageName: String): String {
        val base = systemName?.takeIf { it.isNotBlank() } ?: model
        return if (packageName.endsWith(DEBUG_ID_END)) base + DEBUG_SUFFIX else base
    }
}
