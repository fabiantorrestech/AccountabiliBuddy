package com.accountabilibuddy.companion.debug

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import androidx.health.connect.client.HealthConnectClient
import androidx.health.connect.client.records.SleepSessionRecord
import androidx.health.connect.client.records.metadata.Metadata
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import androidx.health.connect.client.time.TimeRangeFilter
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId

/**
 * Debug builds only: `adb shell am broadcast -a com.accountabilibuddy.companion.debug.SAMPLE_SLEEP -p
 * com.accountabilibuddy.companion` writes the last finished night as slept from 00:20 to 06:35 (replacing earlier
 * samples), so reading sleep can be tested.
 */
class SampleSleepReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val pending = goAsync()
        CoroutineScope(Dispatchers.IO).launch {
            try {
                val zone = ZoneId.systemDefault()
                val client = HealthConnectClient.getOrCreate(context)
                val now = Instant.now()
                // Only this app's own records are deleted.
                client.deleteRecords(SleepSessionRecord::class, TimeRangeFilter.between(now.minus(Duration.ofDays(3)), now.plus(Duration.ofDays(1))))

                // The night that has already ended: this morning after 7 AM, otherwise the one before.
                val morning = if (LocalTime.now().isAfter(LocalTime.of(7, 0))) LocalDate.now() else LocalDate.now().minusDays(1)
                val start = morning.atTime(0, 20).atZone(zone)
                val end = morning.atTime(6, 35).atZone(zone)
                val record = SleepSessionRecord(
                    startTime = start.toInstant(),
                    startZoneOffset = start.offset,
                    endTime = end.toInstant(),
                    endZoneOffset = end.offset,
                    metadata = Metadata.manualEntry(),
                    title = "Sample night",
                )
                client.insertRecords(listOf(record))
                Log.i("SampleSleep", "inserted $start - $end")
            } catch (e: Exception) {
                Log.e("SampleSleep", "failed", e)
            } finally {
                pending.finish()
            }
        }
    }
}
