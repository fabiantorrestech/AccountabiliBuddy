package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.goals.GoalHistorySync
import com.accountabilibuddy.shared.sync.SyncBoard
import com.accountabilibuddy.shared.sync.SyncBoardBuilder
import com.accountabilibuddy.shared.sync.SyncDiagnosticsReport
import com.accountabilibuddy.shared.sync.SyncPassGate
import com.accountabilibuddy.shared.sync.SyncTroubleshootRules
import com.accountabilibuddy.shared.sync.SyncReceiptParts
import com.accountabilibuddy.shared.sync.SyncRereadResult
import com.accountabilibuddy.shared.sync.SyncUnreadableFile
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.withContext
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset

/**
 * The actions behind Settings › Devices & sync › Troubleshoot sync (SYNC1c, ADR 86; the PC's `SyncTroubleshootService`): the confirmation board,
 * "Re-read everything", "Send my files again", "Re-read goal history", "Try again" / "Ignore" / "Remove my stale file" on an unreadable file and the
 * diagnostics report. Display and maintenance only: nothing here changes a setting or applies a looser one (everything still goes through the merge
 * rules and their waits), so nothing needs a lock in a focus session; "Ignore" only hides a line and never touches a method's problem line
 * ([SyncCoordinator.hasProblem]), so the repoint guard is unchanged. Each action returns one result line made of counts only. No Android API in
 * here, so the checks drive it with fake sources.
 */
class SyncTroubleshoot(
    private val sync: SyncCoordinator,
    private val sources: () -> List<SyncTroubleshootSource>,
    private val goalHistory: GoalHistorySync?,
    private val now: () -> Instant,
    private val zone: () -> ZoneId,
    private val appVersion: String,
) {
    private val _busy = MutableStateFlow(false)

    /** True while an action runs (the buttons are off meanwhile). */
    val busy: StateFlow<Boolean> = _busy.asStateFlow()

    private var lastRelayReread: Instant = Instant.MIN
    private var lastSendAgain: Instant = Instant.MIN
    private val relayHistoryGate = SyncPassGate(RELAY_REREAD_EVERY)
    private val relayTryGate = SyncPassGate(RELAY_REREAD_EVERY)

    /**
     * Whether a forced goal history pass on the relay (Re-read goal history, and the history part of Send my files again) may start: the relay's
     * quota is shared by every device on one address, so these passes run at most once per [RELAY_REREAD_EVERY]. True when the relay is off.
     */
    fun relayHistoryOpen(): Boolean =
        sources().none { it.isLive && it.methodCode == SyncReceiptParts.RELAY } || relayHistoryGate.isOpen(now())

    /** The goal history button's text: "Wait a few minutes" while the relay's gate for forced history passes is shut. */
    fun rereadHistoryText(): String = if (relayHistoryOpen()) "Re-read goal history" else SyncTroubleshootRules.WAIT_TEXT

    private suspend fun safely(action: suspend () -> String): String = SyncTroubleshootRules.safely(action)

    /** The unreadable and leftover files this phone found (the list's source). */
    fun files(): List<SyncUnreadableFile> = sync.unreadable?.shown().orEmpty()

    /** The confirmation board as of now. */
    fun board(): SyncBoard {
        val sent = sync.sendLog?.value ?: return SyncBoard(emptyList(), null)
        val receipts = sync.receipts?.value ?: return SyncBoard(emptyList(), null)
        return SyncBoardBuilder.build(
            utc(now()), sync.deviceId, if (sync.isJoined) sync.ownMethods() else emptyList(), sync.received.value.map { it.file }, receipts, sent, ::expectedInterval,
        )
    }

    /**
     * How often a receiver is expected to write when the method delivers by itself: the folder's is this phone's own interval but never under 15
     * minutes (a folder pass is the system's to schedule), Drive's and the relay's 15 minutes (the slowest idle poll of any device, so a phone that
     * polls slowly is never called quiet by mistake); null for QR and file import (by hand, never "quiet").
     */
    fun expectedInterval(method: String): Duration? = when (method) {
        SyncReceiptParts.FOLDER -> Duration.ofMinutes(maxOf(FOLDER_AT_LEAST_MINUTES, sync.settings.value.folderMinutes.toLong()))
        SyncReceiptParts.DRIVE, SyncReceiptParts.RELAY -> Duration.ofMinutes(DRIVE_RELAY_MINUTES)
        else -> null
    }

    /**
     * "Re-read everything received": the "seen" marks go and every file of each method that is on is read and merged again (merging is
     * idempotent). The relay is paced (30 requests a minute at most), once per [RELAY_REREAD_EVERY], never alongside a goal history pass. Returns
     * the result line.
     */
    suspend fun rereadAll(): String = safely { rereadAllNow() }

    private suspend fun rereadAllNow(): String {
        if (_busy.value) return "Already working."
        if (!sync.isJoined) return "Not syncing: this phone isn't in a device group."
        _busy.value = true
        try {
            val results = ArrayList<SyncRereadResult>()
            var relayNote: String? = null
            for (source in sources().filter { it.isLive }) {
                if (source.methodCode == SyncReceiptParts.RELAY) {
                    val at = now()
                    val since = Duration.between(lastRelayReread, at)
                    if (since < RELAY_REREAD_EVERY) {
                        val minutes = Math.max(1, Math.ceil((RELAY_REREAD_EVERY.seconds - since.seconds) / 60.0).toInt())
                        relayNote = "The relay was re-read a moment ago; it can be re-read again in $minutes min."
                        continue
                    }

                    lastRelayReread = at
                }

                results.add(source.rereadAll())
            }

            sync.syncLogger?.note("reread")
            if (results.isEmpty() && relayNote == null) return "No sync method is on: nothing to re-read."
            val files = results.sumOf { it.files }
            val problems = results.sumOf { it.problems }
            var line = "Re-read $files ${if (files == 1) "file" else "files"} · $problems ${if (problems == 1) "problem" else "problems"}"
            if (results.any { it.stopped }) line += " · stopped early: the relay said too many requests"
            return if (relayNote == null) line else "$line · $relayNote"
        } finally {
            _busy.value = false
        }
    }

    /**
     * "Send my files again now": this phone's sync file is written and put on every method that is on, and its goal history files are marked to be
     * put again and a normal pass runs (the history rules stay: only kept months, the relay's quota and request cap). At most once a minute.
     */
    suspend fun sendAgain(): String = safely { sendAgainNow() }

    private suspend fun sendAgainNow(): String {
        val at = now()
        if (_busy.value) return "Already working."
        if (Duration.between(lastSendAgain, at) < SEND_AGAIN_EVERY) return "Sent a moment ago. Try again in a minute."
        if (!sync.isJoined) return "Not syncing: this phone isn't in a device group."
        _busy.value = true
        lastSendAgain = at
        try {
            var sent = 0
            var failed = 0
            for (source in sources().filter { it.isLive }) {
                if (source.writeNow()) sent++ else failed++
            }

            val history = sync.sharesTaskLists() && sources().any { it.isLive && it.methodCode in HISTORY_METHODS }
            val relayHistoryWaits = if (sync.sharesTaskLists()) resendHistory() else false
            sync.syncLogger?.note("resend")
            if (sent + failed == 0) return "No sync method is on: nothing to send."
            val historyText = when {
                relayHistoryWaits -> " · relay goal history can go again in a few minutes"
                history -> " · goal history goes by its normal pass"
                else -> ""
            }
            return "Sent to $sent ${if (sent == 1) "place" else "places"} · $failed failed$historyText"
        } finally {
            _busy.value = false
        }
    }

    // Returns true when the relay's part was left for later (the 5-minute gate).
    private suspend fun resendHistory(): Boolean {
        val history = goalHistory ?: return false
        var waits = false
        for (source in sources().filter { it.isLive && it.methodCode in HISTORY_METHODS }) {
            if (source.methodCode == SyncReceiptParts.RELAY) {
                if (!relayHistoryOpen()) {
                    waits = true
                    continue
                }

                relayHistoryGate.mark(now())
            }

            history.resendKept(source.methodCode)
            source.historyNow()
        }

        sync.syncLogger?.note("history-resend")
        return waits
    }

    /**
     * "Re-read goal history (full)": this phone is new to the group again on each method that carries history (every listed history file is read
     * once through the normal pass: its request cap, the relay's quota and the "no download before a goals part merged" gate all stay), then a pass
     * runs. A relay pass stops at its cap and goes on at the next one.
     */
    suspend fun rereadHistory(): String = safely { rereadHistoryNow() }

    private suspend fun rereadHistoryNow(): String {
        if (_busy.value) return "Already working."
        val live = sources().filter { it.isLive && it.methodCode in HISTORY_METHODS }
        if (!sync.isJoined || !sync.sharesTaskLists() || live.isEmpty()) return "Goal history travels by the shared folder, Google Drive or the relay, with task lists shared: none is on."
        _busy.value = true
        try {
            val history = goalHistory
            var relayWaits = false
            for (source in live) {
                if (source.methodCode == SyncReceiptParts.RELAY) {
                    if (!relayHistoryOpen()) {
                        relayWaits = true
                        continue
                    }

                    relayHistoryGate.mark(now())
                }

                history?.forceReread(source.methodCode)
                source.historyNow()
            }

            if (relayWaits && live.none { it.methodCode != SyncReceiptParts.RELAY }) return "$SyncTroubleshootRules.WAIT_TEXT: the relay was read a moment ago."
            sync.syncLogger?.note("history-reread")
            return if (relayWaits) {
                "Goal history read again on the other ways · the relay can be read again in a few minutes"
            } else {
                "Goal history read again · a big history continues in the next pass"
            }
        } finally {
            _busy.value = false
        }
    }

    /** "Try again" on one unreadable file: its "seen" mark goes and its method reads now. */
    suspend fun tryAgain(file: SyncUnreadableFile): String = safely {
        val source = sources().firstOrNull { it.methodCode == file.method && it.isLive } ?: return@safely "That way of syncing is off."
        if (_busy.value) return@safely "Already working."
        val at = now()
        if (source.methodCode == SyncReceiptParts.RELAY) {
            // A relay read is a request on the shared quota: once per five minutes, the same rule as Re-read everything.
            if (!relayTryGate.isOpen(at)) return@safely "$SyncTroubleshootRules.WAIT_TEXT: the relay was read a moment ago."
            relayTryGate.mark(at)
        }

        _busy.value = true
        try {
            source.tryAgain(file.name)
        } finally {
            _busy.value = false
        }

        sync.syncLogger?.note("retry")
        if (sync.unreadable?.find(file.method, file.key) == null) "Read it now: it's fine." else "Still can't be read."
    }

    /** "Ignore": hides the line. Nothing else changes. */
    fun ignore(file: SyncUnreadableFile) {
        try {
            sync.unreadable?.ignore(file.method, file.key)
            sync.syncLogger?.note("ignored")
        } catch (e: Exception) {
            // A line that can't be hidden stays; never a crash.
        }
    }

    /**
     * "Remove my stale file": only this phone's own old-group or extra copy, on the shared folder or Google Drive. Never on the relay (a relay name
     * is unique per device and the relay deletes only when a device leaves, ADR 46), never another device's file and never this phone's current one.
     */
    suspend fun removeStale(file: SyncUnreadableFile): String = safely {
        if (!file.ownStale) return@safely "That isn't this phone's own file."
        val source = sources().firstOrNull { it.methodCode == file.method && it.isLive && it.methodCode != SyncReceiptParts.RELAY } ?: return@safely "That can't be removed from here."
        val removed = source.removeOwnStale(file.name)
        if (removed) sync.syncLogger?.note("stale-removed")
        if (removed) "Removed this phone's old copy." else "Couldn't remove it; try again later."
    }

    /**
     * "Share a diagnostics report": the text is built off the main thread and handed to [share] (the share sheet); false from it, or anything
     * thrown, is one line, never a crash.
     */
    suspend fun shareReport(share: suspend (String) -> Boolean): String = safely {
        val text = withContext(Dispatchers.IO) { report() }
        if (share(text)) "Choose where to send the report." else SyncTroubleshootRules.FAILED_TEXT
    }

    /** The diagnostics report's text ([SyncDiagnosticsReport]). */
    fun report(): String {
        val ordinals = SyncBoardBuilder.ordinals(sync.received.value.map { it.file.device })
        return SyncDiagnosticsReport.build(
            utc(now()), zone(), appVersion, board(), sync.unreadable?.all().orEmpty(), ordinals, sync.syncLogger?.lines().orEmpty(),
        )
    }

    private fun utc(time: Instant): OffsetDateTime = time.atOffset(ZoneOffset.UTC)

    companion object {
        /** The relay is re-read at most this often (its quota is shared by every device on one address). */
        val RELAY_REREAD_EVERY: Duration = Duration.ofMinutes(5)

        /** "Send my files again" runs at most this often. */
        val SEND_AGAIN_EVERY: Duration = Duration.ofMinutes(1)

        private const val FOLDER_AT_LEAST_MINUTES = 15L
        private const val DRIVE_RELAY_MINUTES = 15L
        private val HISTORY_METHODS = listOf(SyncReceiptParts.FOLDER, SyncReceiptParts.DRIVE, SyncReceiptParts.RELAY)
    }
}
