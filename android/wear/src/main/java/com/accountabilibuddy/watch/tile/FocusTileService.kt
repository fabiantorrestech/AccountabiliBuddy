package com.accountabilibuddy.watch.tile

import android.content.ComponentName
import androidx.wear.protolayout.ActionBuilders.launchAction
import androidx.wear.protolayout.ColorBuilders.argb
import androidx.wear.protolayout.DimensionBuilders.degrees
import androidx.wear.protolayout.DimensionBuilders.dp
import androidx.wear.protolayout.DimensionBuilders.expand
import androidx.wear.protolayout.LayoutElementBuilders
import androidx.wear.protolayout.ModifiersBuilders
import androidx.wear.protolayout.TimelineBuilders.TimeInterval
import androidx.wear.protolayout.layout.box
import androidx.wear.protolayout.layout.column
import androidx.wear.protolayout.material3.MaterialScope
import androidx.wear.protolayout.material3.Typography
import androidx.wear.protolayout.material3.primaryLayout
import androidx.wear.protolayout.material3.text
import androidx.wear.protolayout.modifiers.clickable
import androidx.wear.protolayout.types.layoutString
import androidx.wear.tiles.Material3TileService
import androidx.wear.tiles.RequestBuilders.TileRequest
import androidx.wear.tiles.TileBuilders.Tile
import androidx.wear.tiles.tile
import androidx.wear.tiles.timeline
import androidx.wear.tiles.timelineEntry
import com.accountabilibuddy.shared.DRIFT_COLOR
import com.accountabilibuddy.shared.TileArc
import com.accountabilibuddy.shared.TileRing
import com.accountabilibuddy.shared.TodayView
import com.accountabilibuddy.shared.WatchGlance
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.watch.WatchActivity
import com.accountabilibuddy.watch.link.SnapshotStore
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import kotlin.time.Duration.Companion.minutes

/**
 * The swipe-in Tile (ADR 21): today on a 24-hour ring like the app's Today page (the plan's parts in their colours on
 * the outer ring, focus with red drift ticks on the inner one, a white now tick), with today's focus against the plan
 * or the running session in the middle; tapping it opens the app. The now tick moves through one timeline entry per
 * 10 minutes for the next 2 hours (the last one stays until the next drawing); redrawn when a snapshot arrives.
 */
class FocusTileService : Material3TileService() {
    override suspend fun MaterialScope.tileResponse(requestParams: TileRequest): Tile {
        val snapshot = SnapshotStore.snapshot(this@FocusTileService).value
        val zone = ZoneId.systemDefault()
        val start = Instant.now()
        val entries = (0 until ENTRY_COUNT).map { index ->
            val from = start.plus(ENTRY_LENGTH.multipliedBy(index.toLong()))
            val until = from.plus(ENTRY_LENGTH)
            // The last entry has no end: when the watch doesn't ask again for hours (overnight), it keeps showing it
            // instead of going black once every entry has expired.
            val interval = TimeInterval.Builder().setStartMillis(if (index == 0) 0 else from.toEpochMilli())
            if (index < ENTRY_COUNT - 1) {
                interval.setEndMillis(until.toEpochMilli())
            }
            timelineEntry(layoutAt(snapshot, from, zone), interval.build())
        }
        return tile(timeline = timeline(*entries.toTypedArray()), freshness = REFRESH)
    }

    private fun MaterialScope.layoutAt(snapshot: WatchSnapshot?, now: Instant, zone: ZoneId): LayoutElementBuilders.LayoutElement {
        val context = this@FocusTileService
        val glance = WatchGlance.from(snapshot, now, zone)
        val numeric = glance.headline.first().isDigit()
        val view = snapshot?.let { TodayView.from(it, now, zone, use24Hour = false) }
        val arcs = view?.let { lighten(TileRing.arcs(it)) }.orEmpty()
        val nowDegrees = view?.let { TileRing.nowDegrees(it.nowMinute) }
        return primaryLayout(
            titleSlot = { text(glance.title.layoutString) },
            mainSlot = {
                box(
                    ring(arcs.filter { it.ring == 0 }, nowDegrees, inset = 0f),
                    ring(arcs.filter { it.ring == 1 }, nowDegrees, inset = RING_THICKNESS + RING_GAP),
                    ticks(inset = 2 * (RING_THICKNESS + RING_GAP)),
                    column(
                        text(glance.headline.layoutString, typography = if (numeric) Typography.NUMERAL_SMALL else Typography.TITLE_SMALL),
                        text(glance.detail.layoutString, typography = Typography.BODY_SMALL),
                        // How old the data is, when it isn't fresh.
                        text((glance.updated ?: "").layoutString, typography = Typography.BODY_EXTRA_SMALL),
                    ),
                )
            },
            onClick = clickable(launchAction(ComponentName(context, WatchActivity::class.java))),
        )
    }

    /**
     * Keeps the payload small: drift ticks closer than [DRIFT_MERGE_DEGREES] become one arc and only [MAX_DRIFT_ARCS]
     * are kept (the plan's parts and focus sessions are untouched, so the cap never drops them).
     */
    private fun lighten(arcs: List<TileArc>): List<TileArc> {
        val drift = arcs.filter { it.color == DRIFT_COLOR }.sortedBy { it.startDegrees }
        val merged = mutableListOf<TileArc>()
        drift.forEach { tick ->
            val last = merged.lastOrNull()
            if (last != null && tick.startDegrees - (last.startDegrees + last.sweepDegrees) < DRIFT_MERGE_DEGREES) {
                val end = maxOf(last.startDegrees + last.sweepDegrees, tick.startDegrees + tick.sweepDegrees)
                merged[merged.lastIndex] = last.copy(sweepDegrees = end - last.startDegrees)
            } else {
                merged.add(tick)
            }
        }
        return arcs.filter { it.color != DRIFT_COLOR } + merged.take(MAX_DRIFT_ARCS)
    }

    /** One ring: a faint track, its arcs, and the now tick; [inset] moves it inward from the main slot's edge. */
    private fun ring(arcs: List<TileArc>, nowDegrees: Float?, inset: Float): LayoutElementBuilders.LayoutElement {
        val ring = LayoutElementBuilders.Box.Builder()
            .setWidth(expand())
            .setHeight(expand())
            .setModifiers(
                ModifiersBuilders.Modifiers.Builder()
                    .setPadding(ModifiersBuilders.Padding.Builder().setAll(dp(inset)).build())
                    .build(),
            )
            .addContent(arc(0f, FULL_TURN, TRACK_COLOR, RING_THICKNESS))
        // Many drifts in a day: keep the layout small (the plan and focus come first in the list).
        arcs.take(MAX_ARCS_PER_RING).forEach { ring.addContent(arc(it.startDegrees, it.sweepDegrees, it.color.toInt(), RING_THICKNESS)) }
        nowDegrees?.let { ring.addContent(arc(it - NOW_TICK_DEGREES / 2, NOW_TICK_DEGREES, NOW_COLOR, RING_THICKNESS)) }
        return ring.build()
    }

    /** Clock ticks hanging inward from just inside the focus ring: each hour, longer and brighter every 3 hours. */
    private fun ticks(inset: Float): LayoutElementBuilders.LayoutElement {
        val ring = LayoutElementBuilders.Box.Builder()
            .setWidth(expand())
            .setHeight(expand())
            .setModifiers(
                ModifiersBuilders.Modifiers.Builder()
                    .setPadding(ModifiersBuilders.Padding.Builder().setAll(dp(inset)).build())
                    .build(),
            )
        TileRing.ticks().forEach { tick ->
            val width = if (tick.major) MAJOR_TICK_DEGREES else MINOR_TICK_DEGREES
            ring.addContent(
                arc(tick.degrees - width / 2, width, if (tick.major) MAJOR_TICK_COLOR else MINOR_TICK_COLOR, if (tick.major) MAJOR_TICK_LENGTH else MINOR_TICK_LENGTH),
            )
        }

        return ring.build()
    }

    private fun arc(startDegrees: Float, sweepDegrees: Float, color: Int, thickness: Float): LayoutElementBuilders.LayoutElement =
        LayoutElementBuilders.Arc.Builder()
            .setAnchorAngle(degrees(startDegrees))
            .setAnchorType(LayoutElementBuilders.ARC_ANCHOR_START)
            .addContent(
                LayoutElementBuilders.ArcLine.Builder()
                    .setLength(degrees(sweepDegrees))
                    .setThickness(dp(thickness))
                    .setColor(argb(color))
                    .build(),
            )
            .build()

    private companion object {
        // The system may refresh less often; the timeline keeps the now tick moving meanwhile.
        val REFRESH = 5.minutes
        val ENTRY_LENGTH: Duration = Duration.ofMinutes(10)

        // The Pixel Watch draws nothing (a black Tile) for a timeline of 24 entries; 12 work, whatever their size.
        const val ENTRY_COUNT = 12
        const val RING_THICKNESS = 5f
        const val RING_GAP = 2f
        const val FULL_TURN = 359.9f
        const val NOW_TICK_DEGREES = 2.5f
        const val MAX_ARCS_PER_RING = 32
        const val MAX_DRIFT_ARCS = 16
        const val DRIFT_MERGE_DEGREES = 2f
        const val TRACK_COLOR = 0x33FFFFFF
        const val NOW_COLOR = 0xFFFFFFFF.toInt()
        const val MAJOR_TICK_DEGREES = 1.4f
        const val MINOR_TICK_DEGREES = 0.8f
        const val MAJOR_TICK_LENGTH = 6f
        const val MINOR_TICK_LENGTH = 3f
        const val MAJOR_TICK_COLOR = 0xCCFFFFFF.toInt()
        const val MINOR_TICK_COLOR = 0x66FFFFFF
    }
}
