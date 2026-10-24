package com.accountabilibuddy.companion.usage

import android.app.AppOpsManager
import android.app.usage.UsageEvents
import android.app.usage.UsageEventsQuery
import android.app.usage.UsageStatsManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Process
import com.accountabilibuddy.companion.model.LocalWindow
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.util.concurrent.ConcurrentHashMap

/**
 * Today's phone use from Android's own usage records (Usage access, granted in Settings): which app was in front
 * and when. Read on the phone only. The home screen, system UI and this app are left out.
 */
class PhoneUsageReader(private val context: Context) {
    // Home screens and app names are looked up once per reader (until the app restarts), not on every read.
    private val ignored: Set<String> by lazy { ignoredPackages() }
    private val labels = ConcurrentHashMap<String, String>()

    fun hasAccess(): Boolean {
        val appOps = context.getSystemService(AppOpsManager::class.java)
        // Deprecated in API 36 without a replacement for older versions; still the documented check.
        @Suppress("DEPRECATION")
        val mode = appOps.unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), context.packageName)
        return mode == AppOpsManager.MODE_ALLOWED
    }

    /** @param focus planned focus windows, to work out the time spent on the phone during them. */
    fun read(range: LocalWindow, focus: List<LocalWindow>): PhoneUsage {
        val zone = ZoneId.systemDefault()
        val from = range.start.atZone(zone).toInstant().toEpochMilli()
        val to = minOf(range.end, LocalDateTime.now()).atZone(zone).toInstant().toEpochMilli()
        if (to <= from) {
            return PhoneUsage.Empty
        }

        val events = query(from, to) ?: return PhoneUsage.Empty
        val changes = mutableListOf<UsageIntervals.Change>()
        val event = UsageEvents.Event()
        while (events.hasNextEvent()) {
            events.getNextEvent(event)
            when (event.eventType) {
                UsageEvents.Event.ACTIVITY_RESUMED -> changes.add(UsageIntervals.Change(localTime(event.timeStamp, zone), event.packageName, resumed = true))
                UsageEvents.Event.ACTIVITY_PAUSED -> changes.add(UsageIntervals.Change(localTime(event.timeStamp, zone), event.packageName, resumed = false))
                // The screen went off, the phone locked or shut down: nothing is in front.
                UsageEvents.Event.SCREEN_NON_INTERACTIVE, UsageEvents.Event.KEYGUARD_SHOWN, UsageEvents.Event.DEVICE_SHUTDOWN ->
                    changes.add(UsageIntervals.Change(localTime(event.timeStamp, zone), null, resumed = false))
            }
        }

        val until = LocalWindow(range.start, localTime(to, zone))
        val windows = UsageIntervals.windows(changes, until).filterKeys { it !in ignored }
        val apps = windows.map { (pkg, list) ->
            AppUsage(pkg, label(pkg), UsageIntervals.total(list), UsageIntervals.inside(list, focus))
        }.filter { it.total.seconds >= 30 }.sortedByDescending { it.total }

        val all = windows.values.flatten()
        return PhoneUsage(UsageIntervals.total(all), UsageIntervals.inside(all, focus), apps)
    }

    /** The phone's unlocks (pickups) between [from] and [to], oldest first; null without Usage access or before Android 9. */
    /** The app (and its screen) that came to the front last in the 30 seconds before [to]; null without Usage access or none. */
    fun foregroundApp(to: Instant = Instant.now()): Pair<String, String?>? = firstResumedBetween(to.minusSeconds(30), to, last = true)

    /** The first app (and its screen) opened within 20 seconds after [unlock]; null without Usage access or none. */
    fun appAfter(unlock: Instant): Pair<String, String?>? = firstResumedBetween(unlock, unlock.plusSeconds(20), last = false)

    /** The screens (activity class names) of [packageName] opened in the last [days] days, for choosing allowed parts. */
    fun screensOf(packageName: String, days: Long = 10): List<String> {
        if (!hasAccess()) {
            return emptyList()
        }

        val to = Instant.now()
        val events = context.getSystemService(UsageStatsManager::class.java).queryEvents(to.minusSeconds(days * 86_400).toEpochMilli(), to.toEpochMilli()) ?: return emptyList()
        val event = UsageEvents.Event()
        val seen = linkedSetOf<String>()
        while (events.hasNextEvent()) {
            events.getNextEvent(event)
            if (event.eventType == UsageEvents.Event.ACTIVITY_RESUMED && event.packageName == packageName) {
                event.className?.let(seen::add)
            }
        }

        return seen.toList()
    }

    private fun firstResumedBetween(from: Instant, to: Instant, last: Boolean): Pair<String, String?>? {
        if (!hasAccess()) {
            return null
        }

        val events = context.getSystemService(UsageStatsManager::class.java).queryEvents(from.toEpochMilli(), to.toEpochMilli()) ?: return null
        val event = UsageEvents.Event()
        var found: Pair<String, String?>? = null
        while (events.hasNextEvent()) {
            events.getNextEvent(event)
            if (event.eventType == UsageEvents.Event.ACTIVITY_RESUMED && event.packageName != context.packageName) {
                found = event.packageName to event.className
                if (!last) {
                    return found
                }
            }
        }

        return found
    }

    fun unlocks(from: Instant, to: Instant): List<Instant>? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.P || !hasAccess()) {
            return null
        }

        if (!to.isAfter(from)) {
            return emptyList()
        }

        val manager = context.getSystemService(UsageStatsManager::class.java)
        val events = if (Build.VERSION.SDK_INT < Build.VERSION_CODES.VANILLA_ICE_CREAM) {
            manager.queryEvents(from.toEpochMilli(), to.toEpochMilli())
        } else {
            manager.queryEvents(UsageEventsQuery.Builder(from.toEpochMilli(), to.toEpochMilli()).setEventTypes(UsageEvents.Event.KEYGUARD_HIDDEN).build())
        } ?: return emptyList()
        val unlocks = mutableListOf<Instant>()
        val event = UsageEvents.Event()
        while (events.hasNextEvent()) {
            events.getNextEvent(event)
            if (event.eventType == UsageEvents.Event.KEYGUARD_HIDDEN) {
                unlocks += Instant.ofEpochMilli(event.timeStamp)
            }
        }

        return unlocks
    }

    /** Only the event types used above; Android 15+ leaves the others out before handing them over. Null: none. */
    private fun query(from: Long, to: Long): UsageEvents? {
        val manager = context.getSystemService(UsageStatsManager::class.java)
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.VANILLA_ICE_CREAM) {
            return manager.queryEvents(from, to)
        }

        return manager.queryEvents(UsageEventsQuery.Builder(from, to).setEventTypes(*EVENT_TYPES).build())
    }

    private fun localTime(epochMilli: Long, zone: ZoneId): LocalDateTime = LocalDateTime.ofInstant(Instant.ofEpochMilli(epochMilli), zone)

    /** Home screens (every installed one), system UI and this app. */
    private fun ignoredPackages(): Set<String> {
        val homes = context.packageManager.queryIntentActivities(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME), 0)
            // Settings' FallbackHome (shown only while the phone starts) is a home screen with a negative priority;
            // leaving it in would leave out all time spent in Settings.
            .filter { it.priority >= 0 }
            .map { it.activityInfo.packageName }
        return homes.toSet() + setOf(context.packageName, "com.android.systemui", "com.google.android.permissioncontroller")
    }

    private fun label(packageName: String): String = labels.getOrPut(packageName) {
        try {
            val info = context.packageManager.getApplicationInfo(packageName, 0)
            context.packageManager.getApplicationLabel(info).toString()
        } catch (e: PackageManager.NameNotFoundException) {
            packageName
        }
    }

    companion object {
        private val EVENT_TYPES = intArrayOf(
            UsageEvents.Event.ACTIVITY_RESUMED,
            UsageEvents.Event.ACTIVITY_PAUSED,
            UsageEvents.Event.SCREEN_NON_INTERACTIVE,
            UsageEvents.Event.KEYGUARD_SHOWN,
            UsageEvents.Event.DEVICE_SHUTDOWN,
        )
    }
}
