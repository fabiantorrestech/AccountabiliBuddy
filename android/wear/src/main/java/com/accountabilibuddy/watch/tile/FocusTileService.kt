package com.accountabilibuddy.watch.tile

import android.content.ComponentName
import androidx.wear.protolayout.ActionBuilders.launchAction
import androidx.wear.protolayout.layout.box
import androidx.wear.protolayout.layout.column
import androidx.wear.protolayout.material3.MaterialScope
import androidx.wear.protolayout.material3.Typography
import androidx.wear.protolayout.material3.circularProgressIndicator
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
import com.accountabilibuddy.shared.WatchGlance
import com.accountabilibuddy.watch.WatchActivity
import com.accountabilibuddy.watch.link.SnapshotStore
import java.time.Instant
import java.time.ZoneId
import kotlin.time.Duration.Companion.minutes

/**
 * The swipe-in Tile (ADR 21): a ring with today's focus against the plan, or the running session's progress and time
 * left; tapping it opens the app. Redrawn when a snapshot arrives and every few minutes.
 */
class FocusTileService : Material3TileService() {
    override suspend fun MaterialScope.tileResponse(requestParams: TileRequest): Tile {
        val context = this@FocusTileService
        val glance = WatchGlance.from(SnapshotStore.snapshot(context).value, Instant.now(), ZoneId.systemDefault())
        val numeric = glance.headline.first().isDigit()
        val layout = primaryLayout(
            titleSlot = { text(glance.title.layoutString) },
            mainSlot = {
                box(
                    circularProgressIndicator(staticProgress = glance.value / glance.max),
                    column(
                        text(glance.headline.layoutString, typography = if (numeric) Typography.NUMERAL_SMALL else Typography.TITLE_SMALL),
                        text(glance.detail.layoutString, typography = Typography.BODY_SMALL),
                    ),
                )
            },
            onClick = clickable(launchAction(ComponentName(context, WatchActivity::class.java))),
        )
        return tile(timeline = timeline(timelineEntry(layout)), freshness = REFRESH)
    }

    private companion object {
        // A running session's minutes move; the system may refresh less often.
        val REFRESH = 5.minutes
    }
}
