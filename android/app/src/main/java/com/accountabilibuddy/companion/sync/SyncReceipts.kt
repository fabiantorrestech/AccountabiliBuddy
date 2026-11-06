package com.accountabilibuddy.companion.sync

import android.content.Context
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.notify.Channels
import com.accountabilibuddy.companion.notify.TimelineNotifier
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.util.UUID

/**
 * Sync receipts, a developer switch (preferences "devSyncReceipts", off by default): a quiet notification when another device's
 * file shows it now holds a newer file of this phone's. Read from the device card's existing `seen` map, so it works the same for
 * the relay, Google Drive, the shared folder and QR codes, and adds nothing to any file. A watch writes no file, so it never confirms.
 * A device seen for the first time since the app started is noted without a notification.
 */
class SyncReceipts(private val context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val confirmed = mutableMapOf<UUID, Instant>()

    var isOn: Boolean
        get() = prefs.getBoolean(KEY_ON, false)
        set(value) = prefs.edit().putBoolean(KEY_ON, value).apply()

    /** Looks at the received files again (call when they changed); notifies only while on. */
    @Synchronized
    fun check(me: UUID, received: List<ReceivedDevice>) {
        val devices = received.filter { it.file.device.kind != SyncDeviceCard.KIND_WATCH }
        devices.forEach { device ->
            val id = device.file.device.deviceId
            val seen = device.file.device.seen[me] ?: return@forEach
            val before = confirmed[id]
            confirmed[id] = seen
            if (!isOn || before == null || !seen.isAfter(before)) return@forEach

            val notYet = devices.filter { other ->
                other.file.device.deviceId != id && other.file.device.seen[me]?.isBefore(seen) != false
            }.map { it.file.device.name }
            notify(
                "Sync reached ${device.file.device.name}",
                "It has this phone's file from ${clock(seen)} (came back by ${device.method} at ${clock(device.receivedAt)})." +
                    if (notYet.isEmpty()) " Every device has it." else " Not yet: ${notYet.joinToString(", ")}.",
            )
        }
    }

    private fun notify(title: String, text: String) {
        if (!TimelineNotifier.canPost(context)) return
        val notification = NotificationCompat.Builder(context, Channels.SYNC_RECEIPTS)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setSilent(true)
            .setAutoCancel(true)
            .build()
        runCatching { NotificationManagerCompat.from(context).notify(NOTIFICATION_ID, notification) }
    }

    private fun clock(time: Instant): String = ClockFormat(is24Hour = false).time(LocalDateTime.ofInstant(time, ZoneId.systemDefault()))

    companion object {
        const val PREFS = "devSyncReceipts"
        private const val KEY_ON = "on"
        private const val NOTIFICATION_ID = 71
    }
}
