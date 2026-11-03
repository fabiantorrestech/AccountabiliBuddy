package com.accountabilibuddy.companion.health

import android.content.Context
import androidx.health.connect.client.HealthConnectClient
import androidx.health.connect.client.permission.HealthPermission
import androidx.health.connect.client.records.StepsRecord
import androidx.health.connect.client.request.AggregateGroupByPeriodRequest
import androidx.health.connect.client.time.TimeRangeFilter
import com.accountabilibuddy.shared.sync.SyncStepDay
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.Period

/**
 * Reads daily step counts from Health Connect (the phone's own health store). Read only, on the phone, and only while "Track daily steps"
 * is on. Uses the aggregate call, which Health Connect de-duplicates across apps and devices, so two sources are not counted twice.
 * Health Connect allows reads only while the app is in front (no READ_HEALTH_DATA_IN_BACKGROUND here), as with sleep.
 */
class StepsReader(private val context: Context) {
    val permissions: Set<String> = setOf(HealthPermission.getReadPermission(StepsRecord::class))

    /** True when Health Connect is on the phone and ready. */
    fun isAvailable(): Boolean = HealthConnectClient.getSdkStatus(context) == HealthConnectClient.SDK_AVAILABLE

    suspend fun hasPermission(): Boolean =
        isAvailable() && client().permissionController.getGrantedPermissions().containsAll(permissions)

    /**
     * The step count of each of the last [days] days up to now, oldest first. A day Health Connect has no steps for is left out, not
     * given as 0, so it shows as "no data".
     */
    suspend fun readDays(days: Int, today: LocalDate = LocalDate.now()): List<SyncStepDay> {
        val request = AggregateGroupByPeriodRequest(
            metrics = setOf(StepsRecord.COUNT_TOTAL),
            timeRangeFilter = TimeRangeFilter.between(today.minusDays(days - 1L).atStartOfDay(), LocalDateTime.now()),
            timeRangeSlicer = Period.ofDays(1),
        )
        return client().aggregateGroupByPeriod(request)
            .mapNotNull { slice ->
                slice.result[StepsRecord.COUNT_TOTAL]?.let { total ->
                    SyncStepDay(slice.startTime.toLocalDate(), total.coerceIn(0L, MAX_COUNT).toInt())
                }
            }
            .sortedBy { it.date }
    }

    private fun client(): HealthConnectClient = HealthConnectClient.getOrCreate(context)

    private companion object {
        const val MAX_COUNT = 1_000_000L
    }
}
