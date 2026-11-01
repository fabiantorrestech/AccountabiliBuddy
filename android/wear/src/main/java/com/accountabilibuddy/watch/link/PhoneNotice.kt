package com.accountabilibuddy.watch.link

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** What the watch has to tell the user about its phone link (ADR 54), on a page of its own. */
enum class PhoneNoticeKind {
    /** The phone was removed from the user's synced devices and waits for the user's choice there (W3); the snapshot stays. */
    PhoneUnlinked,

    /** The phone unlinked this watch (it unlinked it, or started from scratch): forget what the watch shows, or keep it (W4). */
    UnlinkedByPhone,
}

/**
 * The two things the phone can tell the watch about the link, kept in preferences so they survive a restart, and as a flow for the
 * screens. Nothing private travels: [PhoneUnlinked][PhoneNoticeKind.PhoneUnlinked] is the phone's one-byte flag, and the unlink message
 * is the one the watch always got (it used to wipe at once; now the user picks). "Keep for now" keeps the snapshot and silences the
 * unlink message until the phone sends a linked snapshot again.
 */
object PhoneNotice {
    private const val PREFS = "phoneNotice"
    private const val KEY_PHONE_UNLINKED = "phoneUnlinked"
    private const val KEY_UNLINKED_BY_PHONE = "unlinkedByPhone"
    private const val KEY_KEPT = "kept"

    private val _notice = MutableStateFlow<PhoneNoticeKind?>(null)
    @Volatile private var loaded = false

    /** The notice to show, or null; [PhoneNoticeKind.UnlinkedByPhone] comes before [PhoneNoticeKind.PhoneUnlinked]. */
    fun notice(context: Context): StateFlow<PhoneNoticeKind?> {
        load(context)
        return _notice.asStateFlow()
    }

    /** The phone says whether it is unlinked from its synced devices. */
    fun phoneUnlinked(context: Context, unlinked: Boolean) {
        prefs(context).edit().putBoolean(KEY_PHONE_UNLINKED, unlinked).apply()
        publish(context)
    }

    /** The phone unlinked this watch; shown unless the user already chose to keep the data. */
    fun unlinkedByPhone(context: Context) {
        val prefs = prefs(context)
        if (prefs.getBoolean(KEY_KEPT, false)) {
            return
        }

        prefs.edit().putBoolean(KEY_UNLINKED_BY_PHONE, true).apply()
        publish(context)
    }

    /** "Keep for now": the data stays, and the prompt does not come back until the phone links the watch again. */
    fun keepForNow(context: Context) {
        // A watch the phone has unlinked has no phone state left to show: the "phone unlinked" page goes with the prompt (it would
        // otherwise never clear, since a phone that no longer has this watch linked never sends the 0).
        prefs(context).edit().putBoolean(KEY_UNLINKED_BY_PHONE, false).putBoolean(KEY_PHONE_UNLINKED, false).putBoolean(KEY_KEPT, true).apply()
        publish(context)
    }

    /** "Forget data": the snapshot goes, and so does the prompt. */
    fun forget(context: Context) {
        prefs(context).edit().putBoolean(KEY_UNLINKED_BY_PHONE, false).putBoolean(KEY_PHONE_UNLINKED, false).putBoolean(KEY_KEPT, false).apply()
        SnapshotStore.clear(context)
        publish(context)
    }

    /** A snapshot of a linked watch arrived: the phone has linked this watch again, so an earlier unlink no longer applies. */
    fun linkedSnapshot(context: Context) {
        val prefs = prefs(context)
        if (prefs.getBoolean(KEY_UNLINKED_BY_PHONE, false) || prefs.getBoolean(KEY_KEPT, false)) {
            prefs.edit().putBoolean(KEY_UNLINKED_BY_PHONE, false).putBoolean(KEY_KEPT, false).apply()
            publish(context)
        }
    }

    private fun load(context: Context) {
        if (!loaded) {
            loaded = true
            publish(context)
        }
    }

    private fun publish(context: Context) {
        val prefs = prefs(context)
        _notice.value = when {
            prefs.getBoolean(KEY_UNLINKED_BY_PHONE, false) -> PhoneNoticeKind.UnlinkedByPhone
            prefs.getBoolean(KEY_PHONE_UNLINKED, false) -> PhoneNoticeKind.PhoneUnlinked
            else -> null
        }
    }

    private fun prefs(context: Context) = context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
