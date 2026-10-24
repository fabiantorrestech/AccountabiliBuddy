package com.accountabilibuddy.companion.backup

import android.content.Context
import android.content.SharedPreferences
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.provider.DocumentsContract
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/**
 * Auto-backup of the phone's settings ([PhoneBackup]) to a folder the user picks (prefs "backup"): when the app is
 * updated, and a minute after a backed-up setting changes (several changes, one file). Keeps the 10 newest.
 */
class AutoBackup(private val context: Context, private val appVersion: String) {
    private val prefs = context.getSharedPreferences("backup", Context.MODE_PRIVATE)
    private val handler = Handler(Looper.getMainLooper())
    private val pending = Runnable { backUpNow("setting changed") }
    private val _status = MutableStateFlow(prefs.getString(KEY_LAST, null))

    /** "Backed up 4:05 PM (setting changed)" or why not; null before the first. */
    val status: StateFlow<String?> = _status.asStateFlow()

    // Held here: SharedPreferences keeps its listeners weakly.
    private val listener = SharedPreferences.OnSharedPreferenceChangeListener { _, _ -> soon() }

    var folder: Uri?
        get() = prefs.getString(KEY_FOLDER, null)?.let(Uri::parse)
        set(value) = prefs.edit().putString(KEY_FOLDER, value?.toString()).apply()

    /** Watches the backed-up preferences for changes. */
    fun start() {
        PhoneBackup.PREFS.forEach { context.getSharedPreferences(it, Context.MODE_PRIVATE).registerOnSharedPreferenceChangeListener(listener) }
    }

    /** A backed-up setting changed (or a settings file was saved): back up in a minute. */
    fun soon() {
        if (folder == null) return
        handler.removeCallbacks(pending)
        handler.postDelayed(pending, DELAY_MS)
    }

    /** Writes a backup now; [why] goes in the status ("app updated", "setting changed", "by hand"). */
    fun backUpNow(why: String) {
        val tree = folder ?: run {
            report("No backup folder chosen")
            return
        }

        val stamp = LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyy-MM-dd-HHmmss"))
        try {
            val resolver = context.contentResolver
            val parent = DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree))
            val file = DocumentsContract.createDocument(resolver, parent, PhoneBackup.MIME, "$PREFIX$stamp.json") ?: throw java.io.IOException("can't create a file there")
            resolver.openOutputStream(file)?.use { it.write(PhoneBackup.export(context, appVersion).toByteArray()) }
            // The 10 newest stay.
            backups(tree).sortedByDescending { it.second }.drop(KEEP).forEach { (uri, _) -> runCatching { DocumentsContract.deleteDocument(resolver, uri) } }
            report("Backed up ${LocalDateTime.now().format(DateTimeFormatter.ofPattern("MMM d, h:mm a"))} ($why)")
        } catch (e: Exception) {
            Log.w("AutoBackup", "backup failed", e)
            report("Backup failed: ${e.message}")
        }
    }

    // The backups in the folder: their document and name.
    private fun backups(tree: Uri): List<Pair<Uri, String>> {
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree))
        val found = mutableListOf<Pair<Uri, String>>()
        context.contentResolver.query(children, arrayOf(DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME), null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                val name = cursor.getString(1) ?: continue
                if (name.startsWith(PREFIX)) found += DocumentsContract.buildDocumentUriUsingTree(tree, cursor.getString(0)) to name
            }
        }
        return found
    }

    private fun report(text: String) {
        prefs.edit().putString(KEY_LAST, text).apply()
        _status.value = text
    }

    private companion object {
        const val KEY_FOLDER = "folder"
        const val KEY_LAST = "last"
        const val PREFIX = "AccountabiliBuddy-phone-"
        const val KEEP = 10
        const val DELAY_MS = 60_000L
    }
}
