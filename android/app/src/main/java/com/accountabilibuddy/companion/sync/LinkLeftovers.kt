package com.accountabilibuddy.companion.sync

import android.content.Context
import java.io.File

/**
 * The Wi-Fi link's files from before ADR 45, deleted once on the first start after the upgrade: the paired PC (link.json,
 * with the sealed device token and the PC's address) and the focus day the PC sent over the link (focusday.json). The
 * Keystore key that sealed the token stays, since it also seals the sync group code.
 */
object LinkLeftovers {
    private const val PREFS = "linkCleanup"
    private const val KEY_DONE = "done"
    private val FILES = listOf("link.json", "link.json.tmp", "focusday.json")

    fun deleteOnce(context: Context) {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getBoolean(KEY_DONE, false)) {
            return
        }

        val folder = File(context.filesDir, "companion")
        FILES.forEach { File(folder, it).delete() }
        prefs.edit().putBoolean(KEY_DONE, true).apply()
    }
}
