package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.drawable.Drawable
import android.util.LruCache
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * The phone's launchable apps (names only), read once off the main thread and kept, and their icons loaded one by one
 * as rows show them (a few dozen kept). Reading every app with its icon on the main thread froze the essentials screen
 * ("not responding") and the Settings card.
 */
object AppCatalog {
    @Volatile
    private var apps: List<LaunchableApp>? = null
    private val icons = LruCache<String, Drawable>(64)

    /** Every app with a launcher entry, A–Z, this app left out; read again when [refresh] is true. */
    suspend fun apps(context: Context, refresh: Boolean = false): List<LaunchableApp> {
        apps?.takeIf { !refresh }?.let { return it }
        return withContext(Dispatchers.IO) {
            val pm = context.packageManager
            pm.queryIntentActivities(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER), PackageManager.MATCH_ALL)
                .filter { it.activityInfo.packageName != context.packageName }
                .distinctBy { it.activityInfo.packageName }
                .map { LaunchableApp(it.activityInfo.packageName, it.loadLabel(pm).toString(), null) }
                .sortedBy { it.name.lowercase() }
                .also { apps = it }
        }
    }

    /** The app's icon, off the main thread; null when it has none. */
    suspend fun icon(context: Context, packageName: String): Drawable? {
        icons.get(packageName)?.let { return it }
        return withContext(Dispatchers.IO) {
            runCatching { context.packageManager.getApplicationIcon(packageName) }.getOrNull()?.also { icons.put(packageName, it) }
        }
    }
}
