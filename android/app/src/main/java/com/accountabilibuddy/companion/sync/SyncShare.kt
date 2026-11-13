package com.accountabilibuddy.companion.sync

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import androidx.core.content.IntentCompat
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.File
import java.security.MessageDigest

/**
 * Sync files (*.absync) in and out by hand (ADR 22): shares the files the phone carries with Android's share sheet, and
 * reads one the user opened or shared with the app. File names come from the bytes, never from a device's name.
 */
object SyncShare {
    const val MIME_TYPE = "application/octet-stream"
    const val EXTENSION = ".absync"

    // A sync file is a few KB; anything this big isn't one, and isn't read into memory (as on the PC).
    private const val MAX_BYTES = 16 * 1024 * 1024

    /** Opens the share sheet with [files] (sealed sync files). */
    fun share(context: Context, files: List<ByteArray>) {
        val folder = File(context.cacheDir, "sync").apply {
            deleteRecursively()
            mkdirs()
        }
        val uris = files.map { bytes ->
            val file = File(folder, nameOf(bytes))
            file.writeBytes(bytes)
            FileProvider.getUriForFile(context, "${context.packageName}.files", file)
        }
        val send = if (uris.size == 1) {
            Intent(Intent.ACTION_SEND).putExtra(Intent.EXTRA_STREAM, uris[0])
        } else {
            Intent(Intent.ACTION_SEND_MULTIPLE).putParcelableArrayListExtra(Intent.EXTRA_STREAM, ArrayList(uris))
        }
        send.type = MIME_TYPE
        send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        context.startActivity(Intent.createChooser(send, "Share sync files").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }

    /**
     * Opens the share sheet with the sync diagnostics report ([text], SYNC1c): one text file in the app's cache, offered through the FileProvider.
     * Nothing is sent by itself; the file before it is deleted first, and the cache folder is the one the share sheet may read.
     */
    suspend fun shareReport(context: Context, text: String): Boolean {
        // The file is written off the main thread; any failure (a full disk, no app to share with) is false, never a crash.
        val uri = try {
            withContext(Dispatchers.IO) {
                val folder = File(context.cacheDir, "sync").apply { mkdirs() }
                val file = File(folder, "sync-diagnostics.txt")
                file.delete()
                file.writeText(text, Charsets.UTF_8)
                FileProvider.getUriForFile(context, "${context.packageName}.files", file)
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            return false
        }

        return try {
            val send = Intent(Intent.ACTION_SEND)
                .putExtra(Intent.EXTRA_STREAM, uri)
                .setType("text/plain")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            context.startActivity(Intent.createChooser(send, "Share sync report").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            true
        } catch (e: Exception) {
            false
        }
    }

    /** The files in an intent that opened or shared one or more with the app (view, send or send-multiple). */
    fun urisIn(intent: Intent): List<Uri> = when (intent.action) {
        Intent.ACTION_VIEW -> listOfNotNull(intent.data?.takeIf { it.scheme == "content" || it.scheme == "file" })
        Intent.ACTION_SEND -> listOfNotNull(IntentCompat.getParcelableExtra(intent, Intent.EXTRA_STREAM, Uri::class.java))
        Intent.ACTION_SEND_MULTIPLE -> IntentCompat.getParcelableArrayListExtra(intent, Intent.EXTRA_STREAM, Uri::class.java).orEmpty()
        else -> emptyList()
    }

    /** The file's bytes, or null when it's too big or can't be read. */
    fun read(context: Context, uri: Uri): ByteArray? = runCatching {
        context.contentResolver.openInputStream(uri)?.use { input ->
            val out = ByteArrayOutputStream()
            val buffer = ByteArray(64 * 1024)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) {
                    break
                }

                out.write(buffer, 0, count)
                if (out.size() > MAX_BYTES) {
                    return@use null
                }
            }

            out.toByteArray()
        }
    }.getOrNull()

    private fun nameOf(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(bytes).take(8).joinToString("") { "%02x".format(it) } + EXTENSION
}
