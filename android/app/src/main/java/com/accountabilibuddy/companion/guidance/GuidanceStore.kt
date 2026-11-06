package com.accountabilibuddy.companion.guidance

import android.content.Context
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncGuidance
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Duration
import java.time.Instant

/**
 * The guidance mode on this phone (ADR 68), kept in preferences "guidance" ([GuidanceState]). A fresh install starts in ProductaBuddy,
 * an upgraded one in AccountabiliBuddy, both as defaults nobody chose. [onChanged] runs after every change (the sync file, the
 * refresher and the brand name follow).
 */
class GuidanceStore(private val context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val _state = MutableStateFlow(load())

    val state: StateFlow<GuidanceState> = _state.asStateFlow()

    /** ProductaBuddy is in effect: the phone's guards are quiet, as when paused. */
    val isCompanion: Boolean get() = _state.value.mode == GuidanceMode.Companion

    /** Whether a focus session runs at an instant (set by the app); a switch whose wait is over waits for it to end. */
    @Volatile
    var sessionRunning: (Instant) -> Boolean = { false }

    /** Runs after every change. Set by the app. */
    @Volatile
    var onChanged: () -> Unit = {}

    /** Asks for ProductaBuddy; false when refused (a focus session runs). */
    @Synchronized
    fun requestCompanion(now: Instant = TrustedTime.now()): Boolean {
        val next = GuidanceRules.requestCompanion(_state.value, now, DELAY, sessionRunning(now)) ?: return false
        save(next)
        return true
    }

    /** During a session, "after this session ends". */
    @Synchronized
    fun requestCompanionAfterSession(now: Instant = TrustedTime.now()) = save(GuidanceRules.requestCompanionAfterSession(_state.value, now, DELAY))

    /**
     * During a session, "now", once what ending it early costs was paid ([reason] typed; empty for a session that ends on command):
     * the wait starts now and the switch applies with the session still running. The reason is kept sealed on this phone for 90 days.
     */
    @Synchronized
    fun requestCompanionNow(reason: String, now: Instant = TrustedTime.now()) {
        keepReason(reason, now)
        save(GuidanceRules.requestCompanionNow(_state.value, now, DELAY))
    }

    /** "Keep" on the setup page: the starting default becomes a choice and is sent. */
    @Synchronized
    fun confirmDefault(now: Instant = TrustedTime.now()) = save(GuidanceRules.confirmDefault(_state.value, now))

    /** AccountabiliBuddy at once, cancelling a waiting switch. */
    @Synchronized
    fun chooseGuarding(now: Instant = TrustedTime.now()) = save(GuidanceRules.chooseGuarding(now))

    /** Applies a waiting switch once due and no session runs; call on each refresh. */
    @Synchronized
    fun advance(now: Instant = TrustedTime.now()) = save(GuidanceRules.applyIfDue(_state.value, now, sessionRunning(now)))

    /** Takes the newest choice among the other devices' files. */
    @Synchronized
    fun receive(files: List<SyncFile>, now: Instant = TrustedTime.now()) {
        var next = _state.value
        files.mapNotNull { it.guidance }.sortedBy { it.editedAt }.forEach { next = GuidanceRules.receive(next, it, now, DELAY) }
        save(next)
    }

    fun forSync(): SyncGuidance? = GuidanceRules.forSync(_state.value)

    private fun save(next: GuidanceState) {
        if (next == _state.value) return
        _state.value = next
        prefs.edit()
            .putString(KEY_MODE, next.mode.name)
            .putString(KEY_EDITED_AT, next.editedAt?.toString())
            .putString(KEY_PENDING_EDITED_AT, next.pending?.editedAt?.toString())
            .putString(KEY_PENDING_APPLY_AT, next.pending?.applyAt?.toString())
            .putBoolean(KEY_PENDING_DURING_SESSION, next.pending?.duringSession == true)
            .apply()
        onChanged()
    }

    // No saved mode: an upgrade (the "versions" preferences name a previous version) keeps guarding; a fresh install starts in
    // ProductaBuddy. Either is saved once, so a later upgrade doesn't mistake this install for an older one.
    private fun load(): GuidanceState {
        val mode = prefs.getString(KEY_MODE, null)
        if (mode == null) {
            val upgraded = context.getSharedPreferences("versions", Context.MODE_PRIVATE).getString("previous", null) != null
            val initial = if (upgraded) GuidanceState.UPGRADED else GuidanceState.FRESH_INSTALL
            prefs.edit().putString(KEY_MODE, initial.mode.name).putString(KEY_EDITED_AT, initial.editedAt?.toString()).apply()
            return initial
        }

        fun time(key: String): Instant? = prefs.getString(key, null)?.let { runCatching { Instant.parse(it) }.getOrNull() }
        val pendingEdited = time(KEY_PENDING_EDITED_AT)
        val pendingApply = time(KEY_PENDING_APPLY_AT)
        val loadedMode = runCatching { GuidanceMode.valueOf(mode) }.getOrDefault(GuidanceMode.Guarding)
        return GuidanceState(
            mode = loadedMode,
            // An upgraded default saved before it carried the stamp (guarding, nobody chose) gets it now, so it is sent.
            editedAt = time(KEY_EDITED_AT) ?: if (loadedMode == GuidanceMode.Guarding) GuidanceState.UPGRADE_STAMP else null,
            pending = if (pendingEdited != null && pendingApply != null) {
                GuidancePending(pendingEdited, pendingApply, prefs.getBoolean(KEY_PENDING_DURING_SESSION, false))
            } else {
                null
            },
        )
    }

    // Why ProductaBuddy was chosen "now" during a session: sealed with a Keystore key, never synced or backed up, 90 days.
    private fun keepReason(reason: String, now: Instant) {
        if (reason.isBlank()) return
        val keepFrom = now.minus(Duration.ofDays(90))
        val kept = (prefs.getStringSet(KEY_REASONS, emptySet()) ?: emptySet())
            .filter { entry -> entry.substringBefore('|').let { runCatching { Instant.parse(it) }.getOrNull() }?.isAfter(keepFrom) == true }
            .toMutableSet()
        kept += "$now|${com.accountabilibuddy.shared.TokenVault.seal(reason.trim(), REASONS_ALIAS)}"
        prefs.edit().putStringSet(KEY_REASONS, kept).apply()
    }

    companion object {
        /** The looser-change delay on the phone (the same 10 minutes the calendar rules wait). */
        val DELAY: Duration = com.accountabilibuddy.companion.calendar.CalendarOverlay.LOOSENING_DELAY

        private const val PREFS = "guidance"
        private const val KEY_MODE = "mode"
        private const val KEY_EDITED_AT = "editedAt"
        private const val KEY_PENDING_EDITED_AT = "pendingEditedAt"
        private const val KEY_PENDING_APPLY_AT = "pendingApplyAt"
        private const val KEY_PENDING_DURING_SESSION = "pendingDuringSession"
        private const val KEY_REASONS = "reasons"
        private const val REASONS_ALIAS = "accountabilibuddy-guidance-reasons"
    }
}
