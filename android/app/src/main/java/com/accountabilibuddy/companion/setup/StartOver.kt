package com.accountabilibuddy.companion.setup

import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.MainActivity
import java.io.File
import java.time.Duration
import kotlin.system.exitProcess

/**
 * "Start from scratch" on the phone (ADR 54), as the PC's Start over: the app is cleared back to what a fresh install would have,
 * and first-run setup opens.
 *
 * It runs in two steps so nothing can write into the folders being cleared: [prepare] only leaves a marker (the time, in
 * `noBackupFilesDir`, which the clearing never touches) and [restart] relaunches the app in a new process; [runPending] then runs from
 * `Application.attachBaseContext`, before any content provider, store or loop has opened a file, and clears.
 *
 * What is cleared (the app's own folders, nothing else): `files/` (the identity, sync links, received devices, past keys, removed
 * list, stores), `cache/`, `code_cache/`, `shared_prefs/`, `databases/` (WorkManager's), `datastore/`, and the app's external files
 * and cache folders; and the folder permissions the user granted (sync folder, noise folder, backup folder) are released. What a fresh
 * install would also have stays: the Android Keystore keys (the new identity seals with them again; the old sealed values are gone),
 * Android's permission grants, the Google Drive grant held by Play services (revoke it in the Google account), Curbox and every other
 * app, and the user's other devices. The same marker rules as the PC: younger than 10 minutes, else it is deleted and nothing is cleared.
 */
object StartOver {
    /** What the user types to confirm. */
    const val PHRASE = "start over"

    private const val MARKER = "start-over.pending"
    private val FRESH_FOR: Duration = Duration.ofMinutes(10)

    private fun marker(context: Context) = File(context.noBackupFilesDir, MARKER)

    /** Leaves the marker: the next start of the app clears it. False when the marker could not be written (nothing will be cleared). */
    fun prepare(context: Context): Boolean = runCatching {
        val file = marker(context)
        file.parentFile?.mkdirs()
        file.writeText(System.currentTimeMillis().toString())
        true
    }.getOrDefault(false)

    /** Takes the marker back (the restart did not happen). */
    fun cancel(context: Context) {
        marker(context).delete()
    }

    /**
     * Starts the app again in a new process and ends this one; the new process clears first ([runPending]). Does not return.
     */
    fun restart(context: Context): Nothing {
        context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK))
        exitProcess(0)
    }

    /**
     * Clears the app when a fresh marker is there. Called first thing, from `Application.attachBaseContext`. The marker goes last, so a
     * clearing that was cut off runs again at the next start (while the marker is still fresh).
     *
     * @return True when the app was cleared.
     */
    fun runPending(context: Context): Boolean {
        val file = marker(context)
        if (!file.exists()) {
            return false
        }

        val writtenAt = runCatching { file.readText().trim().toLong() }.getOrNull()
        val age = writtenAt?.let { Duration.ofMillis(System.currentTimeMillis() - it) }
        if (age == null || age.isNegative || age > FRESH_FOR) {
            // Old, or not ours: nothing is cleared, and the marker is not left to surprise a later start.
            file.delete()
            return false
        }

        releaseFolderPermissions(context)
        val data = context.dataDir
        listOfNotNull(
            context.filesDir, context.cacheDir, context.codeCacheDir,
            File(data, "shared_prefs"), File(data, "databases"), File(data, "datastore"),
            context.getExternalFilesDir(null), context.externalCacheDir,
        ).forEach(::clearInside)
        file.delete()
        return true
    }

    // The folders the user picked (sync, noise, backup) are not the app's to keep after a fresh start.
    private fun releaseFolderPermissions(context: Context) {
        val flags = Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
        runCatching {
            context.contentResolver.persistedUriPermissions.forEach { permission ->
                runCatching { context.contentResolver.releasePersistableUriPermission(permission.uri, flags) }
            }
        }
    }

    // Everything in the folder, not the folder itself.
    private fun clearInside(folder: File) {
        folder.listFiles()?.forEach { it.deleteRecursively() }
    }
}
