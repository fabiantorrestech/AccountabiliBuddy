package com.accountabilibuddy.companion.calendar

import com.accountabilibuddy.companion.data.SettingsJson
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import kotlinx.serialization.json.JsonArray
import java.util.IdentityHashMap

/**
 * The calendar events of each PC's sync file (ADR 58), parsed once per file instead of on every read: a file that has not changed is the
 * same object, so it is found by identity. Keeps every PC's events, so one PC's are never dropped because another PC wrote later.
 */
class PcFileEventsCache {
    private val parsed = IdentityHashMap<JsonArray, List<CalendarEvent>>()

    @Synchronized
    fun filesOf(devices: List<ReceivedDevice>): List<PcCalendarFile> {
        val files = devices
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC && it.file.events != null }
            .map { device ->
                val json = device.file.events!!
                val events = parsed.getOrPut(json) { SettingsJson.events(json) }
                PcCalendarFile(device.file.device.deviceId.toString(), device.file.device.writtenAt, events)
            }
        val live = devices.mapNotNull { it.file.events }
        parsed.keys.retainAll { key -> live.any { it === key } }
        return files
    }
}
