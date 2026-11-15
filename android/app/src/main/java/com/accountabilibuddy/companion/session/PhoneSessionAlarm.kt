package com.accountabilibuddy.companion.session

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.SystemClock

/**
 * The phone session's two alarms (ADR 85): the set end and an armed end whose wait is over. They count on the ELAPSED clock, from the session's
 * trusted time to the target, so moving the phone's wall clock neither fires one early nor holds it back (the way `LunchStore` does it).
 * Exact when Android allows it (`USE_EXACT_ALARM` is declared), else the nearest inexact one that still wakes an idle phone: an armed end may then
 * apply late, never early. Android drops alarms at a reboot: [PhoneSessionController.onStart] asks again.
 */
class PhoneSessionAlarm(private val context: Context) {
    fun schedule(kind: PhoneAlarmKind, afterMs: Long?) {
        val alarms = context.getSystemService(AlarmManager::class.java) ?: return
        val intent = PendingIntent.getBroadcast(
            context, request(kind),
            Intent(context, PhoneSessionAlarmReceiver::class.java).setAction(action(kind)),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        if (afterMs == null) {
            alarms.cancel(intent)
            return
        }

        val at = SystemClock.elapsedRealtime() + afterMs
        runCatching {
            // canScheduleExactAlarms() exists from Android 12 (S3): before that exact alarms need no permission.
            val exact = kind != PhoneAlarmKind.HEARTBEAT && (Build.VERSION.SDK_INT < Build.VERSION_CODES.S || alarms.canScheduleExactAlarms())
            if (exact) {
                alarms.setExactAndAllowWhileIdle(AlarmManager.ELAPSED_REALTIME_WAKEUP, at, intent)
            } else {
                alarms.setAndAllowWhileIdle(AlarmManager.ELAPSED_REALTIME_WAKEUP, at, intent)
            }
        }
    }

    companion object {
        const val ACTION_END = "com.accountabilibuddy.companion.PHONE_SESSION_END"
        const val ACTION_ARMED = "com.accountabilibuddy.companion.PHONE_SESSION_ARMED"
        const val ACTION_HEARTBEAT = "com.accountabilibuddy.companion.PHONE_SESSION_HEARTBEAT"
        private const val REQUEST_END = 71
        private const val REQUEST_ARMED = 72
        private const val REQUEST_HEARTBEAT = 73

        private fun request(kind: PhoneAlarmKind): Int = when (kind) {
            PhoneAlarmKind.END -> REQUEST_END
            PhoneAlarmKind.ARMED -> REQUEST_ARMED
            PhoneAlarmKind.HEARTBEAT -> REQUEST_HEARTBEAT
        }

        private fun action(kind: PhoneAlarmKind): String = when (kind) {
            PhoneAlarmKind.END -> ACTION_END
            PhoneAlarmKind.ARMED -> ACTION_ARMED
            PhoneAlarmKind.HEARTBEAT -> ACTION_HEARTBEAT
        }
    }
}
