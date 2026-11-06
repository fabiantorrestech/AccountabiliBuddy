package com.accountabilibuddy.companion.notify

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.Instant
import java.util.UUID

/**
 * Every wait running in the background that the phone can show (ADR 66): its own armed end of a PC session, and each PC's armed
 * ends from that PC's sync file. The pinned notification's header counts the first one down with Cancel, and Today lists them all.
 */
object RunningWaits {
    /** One running wait. [pcId] and [armedId] name a PC's armed end; both null for the phone's own. */
    data class Wait(val key: String, val label: String, val readyAt: Instant, val pcId: UUID?, val armedId: String?, val cancelling: Boolean)

    private const val KEY_PHONE = "phone"
    private const val SEND_REQUEST = 61
    private const val CANCEL_REQUEST_BASE = 62

    /** The waits still running at [now], the first to end first. */
    fun current(app: CompanionApp, now: Instant = Instant.now()): List<Wait> =
        // Empty until the app has set up sync and the PC remote (a refresh can come first, e.g. at boot).
        runCatching { collect(app, now) }.getOrDefault(emptyList())

    private fun collect(app: CompanionApp, now: Instant): List<Wait> {
        val waits = mutableListOf<Wait>()
        app.pcRemote.armed.value?.takeIf { it.sendAt.isAfter(now) }?.let { armed ->
            waits += Wait(KEY_PHONE, "${armed.pcName}'s session ends", armed.sendAt, null, null, cancelling = false)
        }
        val cancelling = app.pcRemote.cancelsSent()
        app.sync.received.value
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC }
            .forEach { pc ->
                pc.file.armedEnds.orEmpty().filter { it.readyAt.isAfter(now) }.forEach { end ->
                    waits += Wait("pc:${end.id}", "${end.label} on ${pc.file.device.name}", end.readyAt, pc.file.device.deviceId, end.id, end.id in cancelling)
                }
            }
        return waits.sortedBy { it.readyAt }
    }

    /** Cancels [key]: the phone's own at once; a PC's by a cancel action in this phone's file. */
    fun cancel(app: CompanionApp, key: String) {
        if (key == KEY_PHONE) {
            app.pcRemote.cancelArmed()
            return
        }

        val wait = current(app).firstOrNull { it.key == key } ?: return
        val pcId = wait.pcId ?: return
        val armedId = wait.armedId ?: return
        app.pcRemote.cancelPcArmed(pcId, armedId)
    }

    /** The notification's Cancel button for [wait]. */
    fun cancelIntent(context: Context, wait: Wait): PendingIntent =
        PendingIntent.getBroadcast(
            context, CANCEL_REQUEST_BASE,
            Intent(context, RunningWaitReceiver::class.java).setAction(RunningWaitReceiver.ACTION_CANCEL).putExtra(RunningWaitReceiver.EXTRA_KEY, wait.key),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )

    /** Wakes the app when the phone's own armed end is due, so it is sent with the app in the background; cleared when none is armed. */
    fun scheduleSend(context: Context, sendAt: Instant?) {
        val alarms = context.getSystemService(AlarmManager::class.java)
        val intent = PendingIntent.getBroadcast(
            context, SEND_REQUEST,
            Intent(context, RunningWaitReceiver::class.java).setAction(RunningWaitReceiver.ACTION_SEND),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        if (sendAt == null) {
            alarms.cancel(intent)
            return
        }

        runCatching { alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, sendAt.toEpochMilli(), intent) }
    }
}
