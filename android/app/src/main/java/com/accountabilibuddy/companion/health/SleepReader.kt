package com.accountabilibuddy.companion.health

import android.content.Context
import androidx.health.connect.client.HealthConnectClient
import androidx.health.connect.client.permission.HealthPermission
import androidx.health.connect.client.records.SleepSessionRecord
import androidx.health.connect.client.request.ReadRecordsRequest
import androidx.health.connect.client.time.TimeRangeFilter
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

    /** Sleep sessions that ended in the last two days, as local times, earliest first. */
    suspend fun recentSleep(now: Instant = Instant.now()): List<LocalWindow> {
        val request = ReadRecordsRequest(
            recordType = SleepSessionRecord::class,
            timeRangeFilter = TimeRangeFilter.between(now.minus(Duration.ofHours(48)), now),
        )
        val zone = ZoneId.systemDefault()
        return client().readRecords(request).records
            .map { LocalWindow(LocalDateTime.ofInstant(it.startTime, zone), LocalDateTime.ofInstant(it.endTime, zone)) }
            .filter { it.end.isAfter(it.start) }
            .sortedBy { it.start }
    }

    private fun client(): HealthConnectClient = HealthConnectClient.getOrCreate(context)
}
