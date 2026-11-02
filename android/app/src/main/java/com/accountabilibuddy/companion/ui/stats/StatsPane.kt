package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.StatsRange
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.stats.StatsViewsState
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import java.time.LocalDate

/**
 * The Stats tab (boards 5d, 19e to 19h): "Stats" with a tune button, a row of chips for the views, the range, and the view itself.
 * It only draws what [model] holds and forwards choices through [actions]; the host owns the model and the saved choices, and nothing
 * here starts, ends or unlocks anything. No Scaffold and no fixed width: the chips ([StatsViewChips]), the sheet ([StatsViewsSheet])
 * and the view's body ([StatsViewContent]) are separate composables a tablet can place in its own columns.
 *
 * @param view The view on show, from the saved choice.
 * @param range The range the person chose, or null for none yet (each view then starts at the one its board shows).
 * @param views The order and hidden views of the chips.
 */
@Composable
fun StatsPane(
    model: StatsPaneModel,
    view: StatsView,
    range: StatsRange?,
    views: StatsViewsState,
    actions: StatsPaneActions,
    modifier: Modifier = Modifier,
) {
    val today = model.data.today
    var sheetOpen by rememberSaveable { mutableStateOf(false) }
    // The day the Overview's Day range shows survives rotation; "follow today" moves on at midnight by itself.
    var pickedDay by rememberSaveable { mutableLongStateOf(FOLLOW_TODAY) }
    val day = if (pickedDay == FOLLOW_TODAY) today else LocalDate.ofEpochDay(pickedDay).coerceIn(model.data.firstDay, today)
    val shownRange = view.coerce(range)

    fun pick(date: LocalDate) {
        pickedDay = if (date == today) FOLLOW_TODAY else date.toEpochDay()
        actions.onSelectView(StatsView.Overview)
        actions.onSelectRange(StatsRange.Day)
    }

    val chips = remember(views, view) { if (view in views.shown) views.shown else views.shown + view }
    LazyColumn(
        modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        item {
            AbPageHeader("Stats") { AbQuietIconButton(Icons.Outlined.Tune, "Choose and order the views", onClick = { sheetOpen = true }) }
        }

        item { StatsViewChips(chips, view, onSelect = actions.onSelectView) }
        if (view.ranges.isNotEmpty() && shownRange != null) {
            item {
                AbSegmented(view.ranges.map { it.label }, view.ranges.indexOf(shownRange), onSelect = { actions.onSelectRange(view.ranges[it]) })
            }
        }

        item { StatsViewContent(model, view, shownRange, day, actions, onPickDay = ::pick) }
    }

    if (sheetOpen) {
        StatsViewsSheet(
            views, onChange = actions.onViewsChange,
            onOpen = { opened ->
                actions.onSelectView(opened)
                sheetOpen = false
            },
            onDismiss = { sheetOpen = false },
        )
    }
}

private const val FOLLOW_TODAY = Long.MIN_VALUE
