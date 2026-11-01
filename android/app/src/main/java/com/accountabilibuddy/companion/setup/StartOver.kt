package com.accountabilibuddy.companion.setup

import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.MainActivity
import java.io.File
import java.io.IOException
import java.time.Duration
import kotlin.system.exitProcess

/**
 * "Start from scratch" on the phone (ADR 54), as the PC's Start over: the app is cleared back to what a fresh install would have,
 * and first-run setup opens.
 *
 * It runs in steps so nothing can write into the folders being cleared:
 * 1. [prepare] first keeps a local copy of the app's data files (what the PC does with its database; the three newest copies stay),
 *    then leaves a marker (`pending` and the time) in `noBackupFilesDir`, which the clearing never touches.
 * 2. [restart] relaunches the app in a new process.
 * 3. [runPending] runs from `Application.attachBaseContext`, before any content provider, store or loop has opened a file. It rewrites
 *    the marker to `clearing` before the first delete, clears, and deletes the marker last.
 *
 * A `pending` marker younger than 10 minutes is carried out. An older one is not (the user has long moved on): it is dropped, and a
 * notice for the next screen says that nothing was cleared, so the user can ask again. A `clearing` marker is always finished, whatever
 * its age: a clearing that was cut off must not leave a half-cleared app.
 *
 * What is cleared (the app's own folders, nothing else): `files/` (the identity, sync links, received devices, past keys, removed
 * list, stores), `cache/`, `code_cache/`, `shared_prefs/`, `databases/`, `datastore/`, and the app's external files and cache
 * folders; and the folder permissions the user granted (sync folder, noise folder, backup folder) are released. Not cleared:
 * `no_backup/` (the copy kept by [prepare], and WorkManager's own database, whose jobs [prepare]'s caller cancels first), the Android
 * Keystore keys (the new identity seals with them again; the old sealed values are gone), Android's permission grants, the Google Drive
 * grant held by Play services, Curbox and every other app, and the user's other devices. The copy stays on the phone, in app-private
 * storage, and is never sent anywhere; uninstalling the app removes it.
 */
object StartOver {
    /** What the user types to confirm. */
    const val PHRASE = "start over"

    private const val MARKER = "start-over.pending"
    private const val NOTICE = "start-over.notice"
    private const val COPIES = "start-over"
    private const val PENDING = "pending"
    private const val CLEARING = "clearing"

    // How many copies stay, newest first.
    private const val KEPT_COPIES = 3
    private val FRESH_FOR: Duration = Duration.ofMinutes(10)

    private fun marker(context: Context) = File(context.noBackupFilesDir, MARKER)

    /**
     * Keeps a copy of the app's data files and leaves the marker: the next start of the app clears. Do it off the main thread.
     *
     * @return Null when ready; otherwise why not (nothing is cleared then, and the marker is not left).
     */
    fun prepare(context: Context): String? {
        try {
            val copies = File(context.noBackupFilesDir, COPIES)
            val copy = File(copies, System.currentTimeMillis().toString().padStart(15, '0'))
            copy.mkdirs()
            copyInside(context.filesDir, File(copy, "files"))
            copyInside(File(context.dataDir, "shared_prefs"), File(copy, "shared_prefs"))
            // The newest few stay; the name is the time, so the order of names is the order of time.
            copies.listFiles()?.sortedByDescending { it.name }?.drop(KEPT_COPIES)?.forEach { it.deleteRecursively() }
            val file = marker(context)
            file.parentFile?.mkdirs()
            file.writeText("$PENDING ${System.currentTimeMillis()}")
            return null
        } catch (e: IOException) {
            return "the phone's storage wouldn't take a copy of the app's files first (${e.message ?: "storage error"})"
        }
    }

    // A folder's contents copied beside it; nothing to copy when the folder isn't there.
    private fun copyInside(from: File, to: File) {
        if (from.exists() && !from.copyRecursively(to, overwrite = true)) {
            throw IOException("copying ${from.name} failed")
        }
    }

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
     * Clears the app when a marker is there (see the class comment for which ones). Called first thing, from `Application.attachBaseContext`.
     *
     * @return True when the app was cleared.
     */
    fun runPending(context: Context): Boolean {
        val file = marker(context)
        if (!file.exists()) {
            return false
        }

        val parts = runCatching { file.readText().trim().split(' ') }.getOrDefault(emptyList())
        val writtenAt = parts.getOrNull(1)?.toLongOrNull()
        val finishing = parts.firstOrNull() == CLEARING
        val fresh = parts.firstOrNull() == PENDING && writtenAt != null &&
            Duration.ofMillis(System.currentTimeMillis() - writtenAt).let { !it.isNegative && it <= FRESH_FOR }
        if (!finishing && !fresh) {
            // A restart that never happened, or a marker that isn't ours: nothing is cleared. Said at the next screen, so the user can ask again.
            file.delete()
            runCatching {
                File(context.noBackupFilesDir, NOTICE).writeText(
                    "Start from scratch didn't finish: the app wasn't restarted in time, so nothing was cleared. Ask again if you still want it.",
                )
            }
            return false
        }

        // From here on the clearing is finished whatever happens, so it is written down first.
        if (!finishing) {
            runCatching { file.writeText("$CLEARING ${System.currentTimeMillis()}") }
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

    /** What the next screen should say about a start from scratch that did not finish, once; null when there is nothing to say. */
    fun takeNotice(context: Context): String? {
        val file = File(context.noBackupFilesDir, NOTICE)
        if (!file.exists()) {
            return null
        }

        val text = runCatching { file.readText() }.getOrNull()
        file.delete()
        return text
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
