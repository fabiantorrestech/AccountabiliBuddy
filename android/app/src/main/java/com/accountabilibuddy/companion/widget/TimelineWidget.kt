package com.accountabilibuddy.companion.widget

import android.content.Context
import android.text.format.DateFormat
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.glance.GlanceId
import androidx.glance.GlanceModifier
import androidx.glance.Image
import androidx.glance.ImageProvider
import androidx.glance.LocalSize
import androidx.glance.action.clickable
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.SizeMode
import androidx.glance.action.actionStartActivity
import androidx.glance.appwidget.action.actionStartService
import androidx.glance.appwidget.cornerRadius
import androidx.glance.appwidget.provideContent
import androidx.glance.background
import androidx.glance.layout.Column
import androidx.glance.layout.fillMaxSize
import androidx.glance.layout.fillMaxWidth
import androidx.glance.layout.padding
import androidx.glance.text.FontWeight
import androidx.glance.text.Text
import androidx.glance.text.TextStyle
import androidx.glance.unit.ColorProvider
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.notify.TimelineNotifier
import com.accountabilibuddy.companion.ui.DayBarPainter
import java.time.LocalDateTime

/** Home-screen widget: today's timeline, what's next and (in the evening) the sleep line. Tapping opens the app. */
class TimelineWidget : GlanceAppWidget() {
    override val sizeMode = SizeMode.Exact

    override suspend fun provideGlance(context: Context, id: GlanceId) {
        val state = (context.applicationContext as CompanionApp).store.state.value
        val clock = ClockFormat(DateFormat.is24HourFormat(context))
        val model = DayBarBuilder(clock).build(LocalDateTime.now(), state.document, state.events, state.dayBar, state.sleep, state.slept)
        val density = context.resources.displayMetrics.density
        val upcoming = Upcoming.items(LocalDateTime.now(), state, clock)
        val noise = (context.applicationContext as CompanionApp).noise
        val noiseState = noise.state.value
        val showsNoise = noise.settings.value.widgetButton && noiseState.hasFolder && noiseState.fileCount > 0
        val toggleNoise = android.content.Intent(context, com.accountabilibuddy.companion.noise.NoiseService::class.java)
            .setAction(com.accountabilibuddy.companion.noise.NoiseService.ACTION_TOGGLE)

        provideContent {
            val size = LocalSize.current
            val barWidth = ((size.width.value - 24) * density).toInt().coerceAtLeast(100)
            // The widget is always dark (its own background), whatever the phone's theme.
            val bar = DayBarPainter.bitmap(model, barWidth, density, dark = true, compact = size.height.value < 110)
            Column(
                modifier = GlanceModifier.fillMaxSize().background(Color(0xE6101418)).cornerRadius(16.dp).padding(12.dp)
                    .clickable(actionStartActivity<MainActivity>()),
            ) {
                Text(
                    text = if (state.hasImport) "${model.dateText} · ${model.statusText}" else "Open the app to import your PC's profiles",
                    maxLines = 1,
                    style = TextStyle(color = ColorProvider(Color.White), fontSize = 13.sp, fontWeight = FontWeight.Medium),
                )
                Image(provider = ImageProvider(bar), contentDescription = "Today's timeline", modifier = GlanceModifier.fillMaxWidth().padding(top = 6.dp))
                model.sleepLine?.let { line ->
                    Text(
                        text = line.text,
                        maxLines = 1,
                        style = TextStyle(color = ColorProvider(Color(TimelineNotifier.sleepColor(line.amount, dark = true))), fontSize = 12.sp),
                    )
                }

                // The noise player's play/pause (Settings → Noise player → Play/pause on the widget).
                if (showsNoise) {
                    Text(
                        text = "${if (noiseState.playing) "⏸︎" else "▶︎"}  ${noiseState.title ?: "Noise"}",
                        maxLines = 1,
                        style = TextStyle(color = ColorProvider(Color(0xFF5EEAD4)), fontSize = 13.sp, fontWeight = FontWeight.Medium),
                        modifier = GlanceModifier.padding(top = 4.dp).clickable(actionStartService(toggleNoise, isForegroundService = true)),
                    )
                }

                // Whatever height is left: what's coming up.
                val rows = ((size.height.value - (if (showsNoise) 140 else 120)) / 20).toInt().coerceIn(0, upcoming.size)
                upcoming.take(rows).forEach { item ->
                    Text(
                        text = item,
                        maxLines = 1,
                        style = TextStyle(color = ColorProvider(Color(0xCCFFFFFF)), fontSize = 12.sp),
                        modifier = GlanceModifier.padding(top = 2.dp),
                    )
                }
            }
        }

    }
}
