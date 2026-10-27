package com.accountabilibuddy.watch.complication

import android.app.PendingIntent
import android.content.Intent
import androidx.wear.watchface.complications.data.ComplicationData
import androidx.wear.watchface.complications.data.ComplicationText
import androidx.wear.watchface.complications.data.ComplicationType
import androidx.wear.watchface.complications.data.CountDownTimeReference
import androidx.wear.watchface.complications.data.CountUpTimeReference
import androidx.wear.watchface.complications.data.PlainComplicationText
import androidx.wear.watchface.complications.data.RangedValueComplicationData
import androidx.wear.watchface.complications.data.ShortTextComplicationData
import androidx.wear.watchface.complications.data.TimeDifferenceComplicationText
import androidx.wear.watchface.complications.data.TimeDifferenceStyle
import androidx.wear.watchface.complications.datasource.ComplicationRequest
import androidx.wear.watchface.complications.datasource.SuspendingComplicationDataSourceService
import com.accountabilibuddy.shared.Glance
import com.accountabilibuddy.shared.RunningSession
import com.accountabilibuddy.shared.WatchGlance
import com.accountabilibuddy.watch.WatchActivity
import com.accountabilibuddy.watch.link.SnapshotStore
import java.time.Instant
import java.time.ZoneId

/**
 * The watch-face complication (ADR 21): a ring (RANGED_VALUE) with today's focus against the plan or the running
 * session's progress, or a short text (SHORT_TEXT) with the time left or focused today. While a focus or break timer
 * runs, its time counts by itself on the watch face (down to the phase's end, or up for a session with no set end),
 * titled with the profile or "Break". Tapping opens the app.
 */
class FocusComplicationService : SuspendingComplicationDataSourceService() {
    override suspend fun onComplicationRequest(request: ComplicationRequest): ComplicationData? {
        val snapshot = SnapshotStore.snapshot(this).value
        val now = Instant.now()
        val running = snapshot?.running?.takeIf { it.isLiveAt(now) }
        return data(request.complicationType, WatchGlance.from(snapshot, now, ZoneId.systemDefault()), running)
    }

    override fun getPreviewData(type: ComplicationType): ComplicationData? =
        data(type, Glance(105f, 240f, "1h 45m", "Today", "1h 45m", "of 4h planned"))

    private fun data(type: ComplicationType, glance: Glance, running: RunningSession? = null): ComplicationData? {
        val description = PlainComplicationText.Builder("${glance.title}: ${glance.headline}, ${glance.detail}").build()
        val text = running?.let(::timer) ?: PlainComplicationText.Builder(glance.shortText).build()
        val title = running?.let { PlainComplicationText.Builder(if (it.onBreak) "Break" else if (it.isPlanning) "Planning" else it.profile ?: "Focus").build() }
        return when (type) {
            ComplicationType.RANGED_VALUE -> RangedValueComplicationData.Builder(glance.value, 0f, glance.max, description)
                .setText(text)
                .apply { title?.let(::setTitle) }
                .setTapAction(openApp())
                .build()

            ComplicationType.SHORT_TEXT -> ShortTextComplicationData.Builder(text, description)
                .apply { title?.let(::setTitle) }
                .setTapAction(openApp())
                .build()

            else -> null
        }
    }

    // The watch face counts it: down to the phase's end ("24:59"), or up from its start for a session with no set end.
    private fun timer(running: RunningSession): ComplicationText {
        if (running.isPlanning) {
            // The PC's planning stage (not focus time) counts down to its own end; past it, it waits for "Ready?" on the PC.
            val planningEnd = running.planningEndsAt
            return if (planningEnd != null && planningEnd.isAfter(java.time.Instant.now())) {
                TimeDifferenceComplicationText.Builder(TimeDifferenceStyle.STOPWATCH, CountDownTimeReference(planningEnd)).build()
            } else {
                PlainComplicationText.Builder("Ready?").build()
            }
        }

        val endsAt = running.endsAt
        return if (endsAt != null) {
            TimeDifferenceComplicationText.Builder(TimeDifferenceStyle.STOPWATCH, CountDownTimeReference(endsAt)).build()
        } else {
            TimeDifferenceComplicationText.Builder(TimeDifferenceStyle.STOPWATCH, CountUpTimeReference(running.startedAt)).build()
        }
    }

    private fun openApp(): PendingIntent =
        PendingIntent.getActivity(this, 0, Intent(this, WatchActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
}
