package com.accountabilibuddy.companion.goals

import android.content.Context
import com.accountabilibuddy.shared.goals.GoalMonthLines
import com.accountabilibuddy.shared.goals.GoalWeekStart
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * The Goals screen's own choices ([GoalDisplay]) in the preferences "goalsUi": this phone only, never synced, in the settings
 * backup's Look group. Loaded once at app start; an unreadable value reads as its default.
 */
object GoalDisplayPrefs {
    const val FILE = "goalsUi"
    private const val VIEW = "view"
    private const val COLLAPSED = "collapsed"
    private const val WEEK_START = "weekStart"
    private const val HEATMAP_LABELS = "heatmapLabels"
    private const val EXTRA_TILES = "extraTiles"
    private const val MONTH_LINES = "monthLines"

    private val _display = MutableStateFlow(GoalDisplay())
    val display: StateFlow<GoalDisplay> = _display.asStateFlow()

    fun load(context: Context) {
        val prefs = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
        _display.value = GoalDisplay(
            view = enumOr(prefs.getString(VIEW, null), GoalListView.ByArea),
            collapsed = prefs.getString(COLLAPSED, null).orEmpty().split(',')
                .mapNotNull { text -> runCatching { UUID.fromString(text.trim()) }.getOrNull() }.toSet(),
            weekStart = enumOr(prefs.getString(WEEK_START, null), GoalWeekStart.Sunday),
            heatmapLabels = enumOr(prefs.getString(HEATMAP_LABELS, null), GoalHeatmapLabels.Some),
            extraTiles = prefs.getBoolean(EXTRA_TILES, false),
            monthLines = GoalMonthLines.fromName(prefs.getString(MONTH_LINES, null)),
        )
    }

    fun setView(context: Context, view: GoalListView) = save(context, _display.value.copy(view = view))

    /** Folds or unfolds one area on this phone. */
    fun setCollapsed(context: Context, areaId: UUID, collapsed: Boolean) {
        val current = _display.value.collapsed
        save(context, _display.value.copy(collapsed = if (collapsed) current + areaId else current - areaId))
    }

    fun setWeekStart(context: Context, weekStart: GoalWeekStart) = save(context, _display.value.copy(weekStart = weekStart))

    fun setHeatmapLabels(context: Context, labels: GoalHeatmapLabels) = save(context, _display.value.copy(heatmapLabels = labels))

    fun setMonthLines(context: Context, lines: GoalMonthLines) = save(context, _display.value.copy(monthLines = lines))

    fun setExtraTiles(context: Context, on: Boolean) = save(context, _display.value.copy(extraTiles = on))

    private fun save(context: Context, display: GoalDisplay) {
        _display.value = display
        context.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit()
            .putString(VIEW, display.view.name)
            .putString(COLLAPSED, display.collapsed.joinToString(","))
            .putString(WEEK_START, display.weekStart.name)
            .putString(HEATMAP_LABELS, display.heatmapLabels.name)
            .putBoolean(EXTRA_TILES, display.extraTiles)
            .putString(MONTH_LINES, display.monthLines.name)
            .apply()
    }

    private inline fun <reified T : Enum<T>> enumOr(text: String?, default: T): T =
        enumValues<T>().firstOrNull { it.name == text } ?: default
}
