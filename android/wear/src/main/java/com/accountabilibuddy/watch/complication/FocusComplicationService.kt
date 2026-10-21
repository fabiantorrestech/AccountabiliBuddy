package com.accountabilibuddy.watch.complication

import android.app.PendingIntent
import android.content.Intent
import androidx.wear.watchface.complications.data.ComplicationData
import androidx.wear.watchface.complications.data.ComplicationType
import androidx.wear.watchface.complications.data.PlainComplicationText
import androidx.wear.watchface.complications.data.RangedValueComplicationData
import androidx.wear.watchface.complications.data.ShortTextComplicationData
import androidx.wear.watchface.complications.datasource.ComplicationRequest
import androidx.wear.watchface.complications.datasource.SuspendingComplicationDataSourceService
import com.accountabilibuddy.shared.Glance
import com.accountabilibuddy.shared.WatchGlance
import com.accountabilibuddy.watch.WatchActivity
import com.accountabilibuddy.watch.link.SnapshotStore
import java.time.Instant
import java.time.ZoneId

/**
 * The watch-face complication (ADR 21): a ring (RANGED_VALUE) with today's focus against the plan or the running
 * session's progress, or a short text (SHORT_TEXT) with the time left or focused today. Tapping opens the app.
 */
class FocusComplicationService : SuspendingComplicationDataSourceService() {
    override suspend fun onComplicationRequest(request: ComplicationRequest): ComplicationData? =
        data(request.complicationType, WatchGlance.from(SnapshotStore.snapshot(this).value, Instant.now(), ZoneId.systemDefault()))

    override fun getPreviewData(type: ComplicationType): ComplicationData? =
        data(type, Glance(105f, 240f, "1h 45m", "Today", "1h 45m", "of 4h planned"))

    private fun data(type: ComplicationType, glance: Glance): ComplicationData? {
        val description = PlainComplicationText.Builder("${glance.title}: ${glance.headline}, ${glance.detail}").build()
        val text = PlainComplicationText.Builder(glance.shortText).build()
        return when (type) {
            ComplicationType.RANGED_VALUE -> RangedValueComplicationData.Builder(glance.value, 0f, glance.max, description)
                .setText(text)
                .setTapAction(openApp())
                .build()

            ComplicationType.SHORT_TEXT -> ShortTextComplicationData.Builder(text, description)
                .setTapAction(openApp())
                .build()

            else -> null
        }
    }

    private fun openApp(): PendingIntent =
        PendingIntent.getActivity(this, 0, Intent(this, WatchActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
}
