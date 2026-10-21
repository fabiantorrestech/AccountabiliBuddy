package com.accountabilibuddy.watch.link

import android.content.ComponentName
import android.content.Context
import androidx.wear.tiles.TileService
import androidx.wear.watchface.complications.datasource.ComplicationDataSourceUpdateRequester
import com.accountabilibuddy.watch.complication.FocusComplicationService
import com.accountabilibuddy.watch.tile.FocusTileService

/** The Tile and the complication, asked to redraw when the watch's snapshot changes. */
object Surfaces {
    fun refresh(context: Context) {
        TileService.getUpdater(context).requestUpdate(FocusTileService::class.java)
        ComplicationDataSourceUpdateRequester.create(context, ComponentName(context, FocusComplicationService::class.java)).requestUpdateAll()
    }
}
