package com.accountabilibuddy.companion.notify

import android.content.Context
import com.accountabilibuddy.companion.engine.BedtimeReminderTimes
import com.accountabilibuddy.shared.ReminderMessages
import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.WatchNode
import com.accountabilibuddy.shared.WatchPaths
import com.accountabilibuddy.shared.WatchRoute
import com.google.android.gms.tasks.Task
import com.google.android.gms.wearable.CapabilityClient
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import java.time.Instant
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * Bedtime reminders between devices (ADR 38): the watch's reminders go to it over the Data Layer ("/reminder"); a
 * dismiss or snooze here goes to the watch at once and to the PCs in this phone's sync file (`reminderActions`, kept a
 * day). Actions from the watch or a PC are applied here (see [received]).
 */
class ReminderRelay(private val context: Context, private val links: StateFlow<WatchLinkSettings>, private val scope: CoroutineScope) {
    private val _actions = MutableStateFlow<List<ReminderMessages.Action>>(emptyList())

    /** This phone's dismisses and snoozes of the last day, for its sync file. */
    val actions: StateFlow<List<ReminderMessages.Action>> = _actions.asStateFlow()

    // Actions already applied here (by night, reminder, action and time), so each is applied once.
    private val applied = mutableSetOf<String>()

    fun toWatch(night: String, due: BedtimeReminderTimes.Due, label: String, text: String) {
        val message = ReminderMessages.Ring(night, due.number, due.count, label, text, due.reminder.alarm)
        scope.launch { sendToWatch(WatchPaths.REMINDER, ReminderMessages.write(message)) }
    }

    fun send(night: String?, number: Int, action: String, until: Instant? = null) {
        night ?: return
        val entry = ReminderMessages.Action(night, number, action, Instant.now(), until)
        applied += entry.key
        _actions.value = (_actions.value + entry).filter { it.at.isAfter(Instant.now().minusSeconds(86_400)) }
        scope.launch { sendToWatch(WatchPaths.REMINDER_ACTION, ReminderMessages.write(entry)) }
        (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).sendSyncFilesSoon()
    }

    /** A dismiss or snooze from the watch or a PC: applied here once (not sent back). */
    fun received(entry: ReminderMessages.Action) {
        if (!applied.add(entry.key)) return
        if (entry.at.isBefore(Instant.now().minusSeconds(3600))) return
        when (entry.action) {
            DISMISS -> BedtimeReminders.dismiss(context, entry.number, fromHere = false)
            SNOOZE -> BedtimeReminders.snooze(context, entry.number, fromHere = false, until = entry.until ?: Instant.now().plus(BedtimeReminders.SNOOZE))
        }
    }

    private suspend fun sendToWatch(path: String, bytes: ByteArray) {
        val settings = links.value
        if (settings.linkedNodeId == null) return
        val nodes = runCatching {
            Wearable.getCapabilityClient(context).getCapability(WatchPaths.WATCH_CAPABILITY, CapabilityClient.FILTER_REACHABLE).await().nodes
                .map { WatchNode(it.id, it.displayName, it.isNearby) }
        }.getOrDefault(emptyList())
        val messages = Wearable.getMessageClient(context)
        WatchRoute.choose(settings, nodes).forEach { runCatching { messages.sendMessage(it.id, path, bytes).await() } }
    }

    // Google Play services tasks, awaited without the coroutines-play-services library (not a dependency).
    private suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { continuation ->
        addOnSuccessListener { continuation.resume(it) }
        addOnFailureListener { continuation.resumeWithException(it) }
        addOnCanceledListener { continuation.cancel() }
    }

    companion object {
        const val DISMISS = ReminderMessages.DISMISS
        const val SNOOZE = ReminderMessages.SNOOZE
    }
}
