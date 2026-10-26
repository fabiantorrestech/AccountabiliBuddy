package com.accountabilibuddy.companion.blocks

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.curbox.CurboxClient
import com.accountabilibuddy.companion.engine.BlockRearm
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.model.BlockRef
import com.accountabilibuddy.companion.notify.Channels
import com.accountabilibuddy.shared.sync.SyncBlocker
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Keeps the day plan's block rules for Curbox on this phone (ADR 37): on each run it turns on or off what the events
 * since the last run say (the day's re-enable time, lift windows), warns before blocks come back on, remembers which
 * blocks were ever on ("every block seen on" is what a day without a chosen list keeps on), keeps a short history and
 * sets an exact alarm for the next event. Runs from the refresher and that alarm; never changes Curbox's settings,
 * only on/off. Off with the "Turn blocks back on" switch.
 */
object BlockGuard {
    const val ACTION_RUN = "com.accountabilibuddy.companion.BLOCK_GUARD"
    private const val PREFS = "blockGuard"
    private const val NOTICE_ID = 50
    private const val HISTORY_MAX = 40
    private val mutex = Mutex()

    fun enabled(context: Context): Boolean = prefs(context).getBoolean("on", true)

    fun setEnabled(context: Context, on: Boolean) {
        prefs(context).edit().putBoolean("on", on).apply()
    }

    /** The blocks Curbox reported last (for the sync file and the editors); null when never read. */
    fun lastBlockers(context: Context): List<SyncBlocker>? = prefs(context).getString("blockers", null)?.let(::readBlockers)

    /** What the guard did, newest first ("Turned Social back on · 12:00 AM"). */
    fun history(context: Context): List<String> =
        prefs(context).getString("history", null)?.let { runCatching { (Json.parseToJsonElement(it) as JsonArray).map { e -> (e as JsonPrimitive).content } }.getOrNull() }.orEmpty()

    suspend fun run(context: Context, now: LocalDateTime = LocalDateTime.now()) = mutex.withLock {
        val app = context.applicationContext as CompanionApp
        val prefs = prefs(context)
        val curbox = CurboxClient(context)
        val read = curbox.blockers() ?: return@withLock
        prefs.edit().putString("blockers", writeBlockers(read)).apply()

        val known = prefs.getStringSet("known", emptySet()).orEmpty().toMutableSet()
        val current = read.map { BlockRearm.Current(BlockRef(BlockRef.PHONE, it.kind, it.id, it.name), it.on, it.locked) }
        val plan = app.store.state.value.document.dayPlan
        // Seen on outside a lift window: part of "every block seen on".
        current.filter { it.on && !BlockRearm.inLift(plan, it.ref, now) }.forEach { known += BlockRearm.key(it.ref) }

        val lastRun = prefs.getLong("lastRun", 0L).takeIf { it > 0 }
            ?.let { LocalDateTime.ofInstant(java.time.Instant.ofEpochMilli(it), ZoneId.systemDefault()) }
            ?.coerceAtLeast(now.minusDays(1))
        if (lastRun != null && enabled(context)) {
            val events = BlockRearm.between(plan, BlockRef.PHONE, lastRun, now)
            for ((block, on) in BlockRearm.toApply(events, current, known)) {
                if (curbox.set(block.ref.kind, block.ref.id, on)) {
                    addHistory(context, "${if (on) "Turned ${block.ref.name} back on" else "Turned ${block.ref.name} off"} · ${ClockFormat(is24Hour = false).time(now)}")
                }
            }
            if (events.any { it.turnOn }) NotificationManagerCompat.from(context).cancel(NOTICE_ID)
            BlockRearm.warningsBetween(plan, BlockRef.PHONE, lastRun, now).lastOrNull()?.let { warning ->
                val off = BlockRearm.targets(warning, current, known).filter { !it.on && !it.locked }
                if (off.isNotEmpty() && warning.at > now) notify(context, warning.at, off.map { it.ref.name })
            }
        }
        prefs.edit()
            .putLong("lastRun", now.atZone(ZoneId.systemDefault()).toInstant().toEpochMilli())
            .putStringSet("known", known)
            .apply()
        schedule(context, if (enabled(context)) BlockRearm.next(plan, BlockRef.PHONE, now) else null)
    }

    private fun notify(context: Context, at: LocalDateTime, names: List<String>) {
        val clock = ClockFormat(is24Hour = false)
        val minutes = java.time.Duration.between(LocalDateTime.now(), at).toMinutes().coerceAtLeast(1)
        val notification = NotificationCompat.Builder(context, Channels.BLOCKS)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle("Blocks come back on in $minutes min (${clock.time(at)})")
            .setContentText(names.joinToString(", "))
            .setContentIntent(
                PendingIntent.getActivity(context, 2, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK), PendingIntent.FLAG_IMMUTABLE),
            )
            .setAutoCancel(true)
            .build()
        runCatching { NotificationManagerCompat.from(context).notify(NOTICE_ID, notification) }
    }

    private fun schedule(context: Context, at: LocalDateTime?) {
        val manager = context.getSystemService(AlarmManager::class.java)
        val pending = PendingIntent.getBroadcast(
            context, 41, Intent(context, BlockGuardReceiver::class.java).setAction(ACTION_RUN),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        manager.cancel(pending)
        val millis = at?.atZone(ZoneId.systemDefault())?.toInstant()?.toEpochMilli() ?: return
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S || manager.canScheduleExactAlarms()) {
            manager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, pending)
        } else {
            manager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, pending)
        }
    }

    private fun addHistory(context: Context, line: String) {
        val list = (listOf(line) + history(context)).take(HISTORY_MAX)
        prefs(context).edit().putString("history", buildJsonArray { list.forEach { add(JsonPrimitive(it)) } }.toString()).apply()
    }

    private fun writeBlockers(list: List<SyncBlocker>): String = buildJsonArray {
        list.forEach { b ->
            add(buildJsonObject {
                put("kind", b.kind)
                put("id", b.id)
                put("name", b.name)
                put("on", b.on)
                put("locked", b.locked)
            })
        }
    }.toString()

    private fun readBlockers(text: String): List<SyncBlocker>? = runCatching {
        (Json.parseToJsonElement(text) as JsonArray).map { element ->
            val o = element as JsonObject
            fun get(key: String) = (o[key] as? JsonPrimitive)?.content
            SyncBlocker(get("kind")!!, get("id")!!, get("name") ?: "", get("on") == "true", get("locked") == "true")
        }
    }.getOrNull()

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
