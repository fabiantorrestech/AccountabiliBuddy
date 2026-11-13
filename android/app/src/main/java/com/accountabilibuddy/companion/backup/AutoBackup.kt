package com.accountabilibuddy.companion.backup

import android.content.SharedPreferences
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.provider.DocumentsContract
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.BackupFile
import com.accountabilibuddy.companion.sync.DriveFiles
import com.accountabilibuddy.companion.sync.DriveNetwork
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonObject
import java.security.MessageDigest
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/**
 * Auto-backup of the phone (ADR 71), one file per kind ([PhoneKindBackup]): synced settings, this phone's settings, and the task lists
 * while they aren't shared (shared, they are in the synced settings). Each has its own switch (prefs "backup", all on). A minute after a
 * change (several changes, one run), on an app update, and by hand, every kind that is on and changed since its last backup is written to
 * the folder the user picked (the 10 newest per kind stay) and, with Drive sync on in a device group, sealed with the group's key to the
 * app's Drive folder (`Settings backups/<kind>/<device>/`, the 10 newest stay). No folder: nothing is written.
 */
class AutoBackup(private val app: CompanionApp) {
    private val prefs = app.getSharedPreferences("backup", android.content.Context.MODE_PRIVATE)
    private val handler = Handler(Looper.getMainLooper())
    private val pending = Runnable { backUpNow("setting changed", force = false) }
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val _status = MutableStateFlow(prefs.getString(KEY_LAST, null))

    /** "Backed up 4:05 PM (setting changed)" or why not; null before the first. */
    val status: StateFlow<String?> = _status.asStateFlow()

    // Held here: SharedPreferences keeps its listeners weakly.
    private val listener = SharedPreferences.OnSharedPreferenceChangeListener { _, _ -> soon() }

    var folder: Uri?
        get() = prefs.getString(KEY_FOLDER, null)?.let(Uri::parse)
        set(value) = prefs.edit().putString(KEY_FOLDER, value?.toString()).apply()

    /** Whether [kind] is backed up automatically (its switch; task lists only while they aren't shared). */
    fun isOn(kind: PhoneKindBackup.Kind): Boolean =
        prefs.getBoolean(KEY_ON + kind.written, true) && (kind != PhoneKindBackup.Kind.Tasks || !app.sync.sharesTaskLists())

    /** [kind]'s own switch, whatever the sharing. */
    fun switchOf(kind: PhoneKindBackup.Kind): Boolean = prefs.getBoolean(KEY_ON + kind.written, true)

    fun setSwitch(kind: PhoneKindBackup.Kind, on: Boolean) {
        prefs.edit().putBoolean(KEY_ON + kind.written, on).apply()
        if (on) soon()
    }

    /** Watches the backed-up preferences for changes. */
    fun start() {
        PhoneBackup.WATCHED_PREFS.forEach { app.getSharedPreferences(it, android.content.Context.MODE_PRIVATE).registerOnSharedPreferenceChangeListener(listener) }
    }

    /** A backed-up setting changed (or a settings file, a task list or the mode): back up in a minute. */
    fun soon() {
        if (folder == null) return
        handler.removeCallbacks(pending)
        handler.postDelayed(pending, DELAY_MS)
    }

    /**
     * Writes every kind that is on now; with [force] (by hand, an update) even when it didn't change. [why] goes in the status ("app
     * updated", "setting changed", "by hand").
     */
    fun backUpNow(why: String, force: Boolean = true) {
        val tree = folder ?: run {
            report("No backup folder chosen")
            return
        }

        val stamp = LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyy-MM-dd-HHmmss"))
        var written = 0
        try {
            // A closed task list (R7.2 review item 2) writes no task-lists backup: one without tasks would push good ones out of the 10 kept.
            for (kind in PhoneKindBackup.Kind.entries.filter(::isOn).filterNot { it == PhoneKindBackup.Kind.Tasks && app.taskModel.isClosed }) {
                val content = PhoneKindBackup.content(app, kind)
                val hash = hash(content)
                if (!force && prefs.getString(KEY_HASH + kind.written, null) == hash) continue
                write(tree, kind, stamp, BackupFile.seal(content))
                toDrive(kind, stamp, content)
                prefs.edit().putString(KEY_HASH + kind.written, hash).apply()
                written++
            }
            report(if (written == 0) "Nothing changed since the last backup" else "Backed up ${LocalDateTime.now().format(DateTimeFormatter.ofPattern("MMM d, h:mm a"))} ($why)")
        } catch (e: Exception) {
            Log.w("AutoBackup", "backup failed", e)
            report("Backup failed: ${e.message}")
        }
    }

    private fun write(tree: Uri, kind: PhoneKindBackup.Kind, stamp: String, text: String) {
        val resolver = app.contentResolver
        val parent = DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree))
        val prefix = "$PREFIX${kind.written}-"
        val file = DocumentsContract.createDocument(resolver, parent, "application/octet-stream", "$prefix$stamp${kind.extension}")
            ?: throw java.io.IOException("can't create a file there")
        resolver.openOutputStream(file)?.use { it.write(text.toByteArray()) }
        // The 10 newest of this kind stay.
        backups(tree, prefix).sortedByDescending { it.second }.drop(KEEP).forEach { (uri, _) -> runCatching { DocumentsContract.deleteDocument(resolver, uri) } }
    }

    // Sealed with the group's key, so Drive holds nothing readable; quietly skipped offline, signed out or outside a group.
    private fun toDrive(kind: PhoneKindBackup.Kind, stamp: String, content: JsonObject) {
        if (!app.sync.settings.value.driveOn || !app.sync.isJoined) return
        val (deviceFolder, sealed) = app.sync.sealSettingsBackup(kind.written, content.toString()) ?: return
        scope.launch {
            runCatching {
                val token = app.driveSignIn.accessToken() ?: return@launch
                DriveFiles(DriveNetwork).uploadSettingsBackup(token, kind.written, deviceFolder, "$stamp.absync", sealed, KEEP)
            }.onFailure { Log.w("AutoBackup", "Drive copy failed", it) }
        }
    }

    // The backups of one kind in the folder: their document and name.
    private fun backups(tree: Uri, prefix: String): List<Pair<Uri, String>> {
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree))
        val found = mutableListOf<Pair<Uri, String>>()
        app.contentResolver.query(children, arrayOf(DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME), null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                val name = cursor.getString(1) ?: continue
                if (name.startsWith(prefix)) found += DocumentsContract.buildDocumentUriUsingTree(tree, cursor.getString(0)) to name
            }
        }
        return found
    }

    // The content without its creation time, so an unchanged kind isn't written again.
    private fun hash(content: JsonObject): String {
        val device = (content["device"] as? JsonObject)?.let { JsonObject(it - "createdAt") }
        val stable = JsonObject((content - "createdAt") + listOfNotNull(device?.let { "device" to it })).toString()
        return MessageDigest.getInstance("SHA-256").digest(stable.toByteArray()).joinToString("") { "%02x".format(it) }
    }

    private fun report(text: String) {
        prefs.edit().putString(KEY_LAST, text).apply()
        _status.value = text
    }

    private companion object {
        const val KEY_FOLDER = "folder"
        const val KEY_LAST = "last"
        const val KEY_ON = "on."
        const val KEY_HASH = "hash."
        const val PREFIX = "AccountabiliBuddy-"
        const val KEEP = 10
        const val DELAY_MS = 60_000L
    }
}
