package com.accountabilibuddy.watch.timers

import android.content.ComponentName
import android.content.Context
import androidx.wear.watchface.complications.datasource.ComplicationDataSourceUpdateRequester
import com.accountabilibuddy.watch.complication.TimersComplicationService
import com.accountabilibuddy.watch.link.SerialBackground
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.time.Duration
import java.time.Instant

/**
 * The watch's own timers (V1), kept in its prefs "timers" and as a flow for the Timers page. Each change sets the
 * exact alarms again ([TimerAlarms]) and redraws the Timers complication. Nothing leaves the watch.
 */
object TimerStore {
    private const val PREFS = "timers"
    private const val LIST = "list"
    private const val NEXT_ID = "nextId"

    private val lock = Any()
    private val effectsLock = Any()
    private val background = SerialBackground("timer-store")
    private val _timers = MutableStateFlow<List<WatchTimer>>(emptyList())
    @Volatile private var loaded = false

    fun timers(context: Context): StateFlow<List<WatchTimer>> {
        load(context)
        return _timers.asStateFlow()
    }

    fun start(context: Context, length: Duration, name: String = WatchTimer.DEFAULT_NAME) = change(context) { timers ->
        val prefs = prefs(context)
        val id = prefs.getInt(NEXT_ID, 1)
        prefs.edit().putInt(NEXT_ID, id + 1).apply()
        timers + WatchTimer(id, name, length, endsAt = Instant.now().plus(length), pausedLeft = null)
    }

    fun pause(context: Context, id: Int) = update(context, id) { it.paused(Instant.now()) }

    fun resume(context: Context, id: Int) = update(context, id) { it.resumed(Instant.now()) }

    fun plusMinute(context: Context, id: Int) = update(context, id) { it.plusMinute(Instant.now()) }

    fun dismiss(context: Context, id: Int) = change(context) { timers -> timers.filterNot { it.id == id } }

    /** After a restart or a timer's end: the alarms and the complication follow the stored timers again. */
    fun refresh(context: Context) {
        load(context)
        synchronized(effectsLock) {
            TimerAlarms.schedule(context, _timers.value)
            requestComplicationUpdate(context)
        }
    }

    private fun update(context: Context, id: Int, transform: (WatchTimer) -> WatchTimer) =
        change(context) { timers -> timers.map { if (it.id == id) transform(it) else it } }

    // The list changes in memory at once (the page follows in the same frame); saving it, the alarms, the notification
    // and the complication follow on a background thread, in the order of the changes, each from the then-latest list.
    private fun change(context: Context, transform: (List<WatchTimer>) -> List<WatchTimer>) {
        val app = context.applicationContext
        synchronized(lock) {
            load(app)
            _timers.value = transform(_timers.value)
        }
        background.run {
            val timers = _timers.value
            prefs(app).edit().putString(LIST, write(timers).toString()).apply()
            applySideEffects(app, timers)
        }
    }

    private fun applySideEffects(context: Context, timers: List<WatchTimer>) = synchronized(effectsLock) {
        TimerAlarms.schedule(context, timers)
        TimerRing.cancelGone(context, timers)
        requestComplicationUpdate(context)
    }

    private fun requestComplicationUpdate(context: Context) {
        ComplicationDataSourceUpdateRequester.create(context, ComponentName(context, TimersComplicationService::class.java)).requestUpdateAll()
    }

    private fun load(context: Context) = synchronized(lock) {
        if (loaded) {
            return@synchronized
        }

        _timers.value = prefs(context).getString(LIST, null)?.let { runCatching { read(JSONArray(it)) }.getOrNull() } ?: emptyList()
        loaded = true
    }

    private fun write(timers: List<WatchTimer>): JSONArray = JSONArray().apply {
        timers.forEach { timer ->
            put(JSONObject().apply {
                put("id", timer.id)
                put("name", timer.name)
                put("lengthMs", timer.length.toMillis())
                timer.endsAt?.let { put("endsAt", it.toEpochMilli()) }
                timer.pausedLeft?.let { put("pausedLeftMs", it.toMillis()) }
            })
        }
    }

    private fun read(array: JSONArray): List<WatchTimer> = (0 until array.length()).map { index ->
        val item = array.getJSONObject(index)
        WatchTimer(
            id = item.getInt("id"),
            name = item.optString("name", WatchTimer.DEFAULT_NAME),
            length = Duration.ofMillis(item.getLong("lengthMs")),
            endsAt = if (item.has("endsAt")) Instant.ofEpochMilli(item.getLong("endsAt")) else null,
            pausedLeft = if (item.has("pausedLeftMs")) Duration.ofMillis(item.getLong("pausedLeftMs")) else null,
        )
    }

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
