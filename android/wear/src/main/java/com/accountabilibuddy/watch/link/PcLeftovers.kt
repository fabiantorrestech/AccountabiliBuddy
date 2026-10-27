package com.accountabilibuddy.watch.link

import android.content.Context
import java.io.File

/**
 * The watch's own PC link from before ADR 45, deleted once on the first start after the upgrade: pc.json held the PC's
 * address, its certificate's fingerprint and the sealed device token. The watch reaches a PC only through the phone now.
 */
object PcLeftovers {
    private const val PREFS = "pcCleanup"
    private const val KEY_DONE = "done"
    private val FILES = listOf("pc.json", "pc.json.tmp")

    fun deleteOnce(context: Context) {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getBoolean(KEY_DONE, false)) {
            return
        }

        FILES.forEach { File(context.filesDir, it).delete() }
        prefs.edit().putBoolean(KEY_DONE, true).apply()
    }
}
