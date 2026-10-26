package com.accountabilibuddy.watch.complication

import android.app.PendingIntent
import androidx.wear.watchface.complications.data.ComplicationData
import androidx.wear.watchface.complications.data.ComplicationType
import androidx.wear.watchface.complications.data.CountDownTimeReference
import androidx.wear.watchface.complications.data.PlainComplicationText
import androidx.wear.watchface.complications.data.RangedValueComplicationData
import androidx.wear.watchface.complications.data.ShortTextComplicationData
import androidx.wear.watchface.complications.data.TimeDifferenceComplicationText
import androidx.wear.watchface.complications.data.TimeDifferenceStyle
import androidx.wear.watchface.complications.datasource.ComplicationRequest
import androidx.wear.watchface.complications.datasource.SuspendingComplicationDataSourceService
import com.accountabilibuddy.watch.WatchActivity
import com.accountabilibuddy.watch.timers.TimerStore
import com.accountabilibuddy.watch.timers.WatchTimer
import java.time.Duration
import java.time.Instant

/**
 * The second watch-face complication, "Timers" (beside the Focus one): the watch's own timer that ends soonest, its time
 * left counting down by itself on the watch face, titled with its name; as a ring (RANGED_VALUE) it's what's left of its
 * length. "None" when none runs. The store asks for an update whenever the timers change and at each end. Tapping
 * opens the Timers page.
 */
class TimersComplicationService : SuspendingComplicationDataSourceService() {
    override suspend fun onComplicationRequest(request: ComplicationRequest): ComplicationData? {
        val now = Instant.now()
        val soonest = TimerStore.timers(this).value.filter { it.isCounting(now) }.minByOrNull { it.endsAt!! }
        return data(request.complicationType, soonest, now)
    }

    override fun getPreviewData(type: ComplicationType): ComplicationData? {
        val now = Instant.now()
        val sample = WatchTimer(0, WatchTimer.DEFAULT_NAME, Duration.ofMinutes(25), now.plus(Duration.ofMinutes(18)), null)
        return data(type, sample, now)
    }

    private fun data(type: ComplicationType, timer: WatchTimer?, now: Instant): ComplicationData? {
        val description = PlainComplicationText.Builder(timer?.let { "${it.name}: ends soonest" } ?: "No timer running").build()
        val text = timer?.let {
            TimeDifferenceComplicationText.Builder(TimeDifferenceStyle.STOPWATCH, CountDownTimeReference(it.endsAt!!)).build()
        } ?: PlainComplicationText.Builder(NO_TIMER).build()
        val title = timer?.let { PlainComplicationText.Builder(it.name).build() }
        return when (type) {
            ComplicationType.RANGED_VALUE -> {
                val length = timer?.length?.seconds?.toFloat() ?: 1f
                val left = timer?.left(now)?.seconds?.toFloat() ?: 0f
                RangedValueComplicationData.Builder(left.coerceIn(0f, length), 0f, length, description)
                    .setText(text)
                    .apply { title?.let(::setTitle) }
                    .setTapAction(openTimers())
                    .build()
            }

            ComplicationType.SHORT_TEXT -> ShortTextComplicationData.Builder(text, description)
                .apply { title?.let(::setTitle) }
                .setTapAction(openTimers())
                .build()

            else -> null
        }
    }

    private fun openTimers(): PendingIntent =
        PendingIntent.getActivity(this, OPEN_TIMERS_REQUEST, WatchActivity.timersIntent(this), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)

    private companion object {
        // Short enough for a small slot; the description says "No timer running".
        const val NO_TIMER = "None"
        // Apart from the Focus complication's open-the-app intent (request 0), so the Timers page extra isn't merged away.
        const val OPEN_TIMERS_REQUEST = 2
    }
}
