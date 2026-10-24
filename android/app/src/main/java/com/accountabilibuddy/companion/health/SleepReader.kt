package com.accountabilibuddy.companion.health

import android.content.Context
import androidx.health.connect.client.HealthConnectClient
import androidx.health.connect.client.permission.HealthPermission
import androidx.health.connect.client.records.SleepSessionRecord
import androidx.health.connect.client.request.ReadRecordsRequest
import androidx.health.connect.client.time.TimeRangeFilter
import com.accountabilibuddy.companion.engine.SleepPieces
import com.accountabilibuddy.companion.model.LocalWindow
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Reads sleep from Health Connect, the phone's own health store (what a Pixel Watch or Fitbit writes there). Read
 * only, on the phone; nothing leaves it.
 */
class SleepReader(private val context: Context) {
    val permissions: Set<String> = setOf(HealthPermission.getReadPermission(SleepSessionRecord::class))

    /** True when Health Connect is on the phone and ready. */
    fun isAvailable(): Boolean = HealthConnectClient.getSdkStatus(context) == HealthConnectClient.SDK_AVAILABLE

    suspend fun hasPermission(): Boolean =
        isAvailable() && client().permissionController.getGrantedPermissions().containsAll(permissions)

    /** Sleep sessions that ended in the last 21 days (the week's average, the smart bedtime), as local times, earliest first. */
    suspend fun recentSleep(now: Instant = Instant.now()): List<LocalWindow> {
        val request = ReadRecordsRequest(
            recordType = SleepSessionRecord::class,
            timeRangeFilter = TimeRangeFilter.between(now.minus(Duration.ofDays(21)), now),
        )
        val zone = ZoneId.systemDefault()
        fun local(at: Instant) = LocalDateTime.ofInstant(at, zone)
        return client().readRecords(request).records
            .flatMap { record ->
                // Awake stretches inside a session (e.g. up at 5:30, asleep again at 6:00) split it (SleepPieces).
                val awake = record.stages
                    .filter { it.stage in AWAKE_STAGES }
                    .map { LocalWindow(local(it.startTime), local(it.endTime)) }
                SleepPieces.split(LocalWindow(local(record.startTime), local(record.endTime)), awake)
            }
            .filter { it.end.isAfter(it.start) }
            .sortedBy { it.start }
    }

    private fun client(): HealthConnectClient = HealthConnectClient.getOrCreate(context)

    private companion object {
        val AWAKE_STAGES = setOf(
            SleepSessionRecord.STAGE_TYPE_AWAKE,
            SleepSessionRecord.STAGE_TYPE_AWAKE_IN_BED,
            SleepSessionRecord.STAGE_TYPE_OUT_OF_BED,
        )
    }
}
