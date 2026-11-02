package com.accountabilibuddy.companion.ui.stats

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.accountabilibuddy.companion.stats.StatsRange
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.stats.StatsViewsState

/**
 * The Stats tab's own choices (preferences "stats"): the view last shown, the range, and the order and hidden views of the chip
 * row. Per device and never synced; nothing about what the numbers say is stored here.
 */
class StatsPrefs(context: Context) {
    private val prefs = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)

    var view: StatsView by mutableStateOf(StatsView.fromKey(prefs.getString(KEY_VIEW, null)) ?: StatsView.Overview)
        private set

    /** The range the person chose; null until they do (each view then shows the range its board shows). */
    var range: StatsRange? by mutableStateOf(runCatching { StatsRange.valueOf(prefs.getString(KEY_RANGE, "") ?: "") }.getOrNull())
        private set

    var views: StatsViewsState by mutableStateOf(StatsViewsState.parse(prefs.getString(KEY_ORDER, null), prefs.getString(KEY_HIDDEN, null)))
        private set

    fun select(next: StatsView) {
        view = next
        prefs.edit().putString(KEY_VIEW, next.key).apply()
    }

    fun chooseRange(next: StatsRange) {
        range = next
        prefs.edit().putString(KEY_RANGE, next.name).apply()
    }

    fun chooseViews(next: StatsViewsState) {
        views = next
        prefs.edit().putString(KEY_ORDER, next.orderText()).putString(KEY_HIDDEN, next.hiddenText()).apply()
    }

    private companion object {
        const val FILE = "stats"
        const val KEY_VIEW = "view"
        const val KEY_RANGE = "range"
        const val KEY_ORDER = "order"
        const val KEY_HIDDEN = "hidden"
    }
}
