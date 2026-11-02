package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
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
    // A tap on a bar, a heat cell or a day opens that day on Overview > Day for now only: it is not saved, so the next launch opens the
    // view and range the person chose. Any choice of their own (a chip, a segment, a tile, the sheet) ends it.
    var dayOpened by rememberSaveable { mutableStateOf(false) }
    // A view chosen from outside (a route) ends it too.
    LaunchedEffect(view) { dayOpened = false }
    val shownView = if (dayOpened) StatsView.Overview else view
    val day = if (pickedDay == FOLLOW_TODAY) today else LocalDate.ofEpochDay(pickedDay).coerceIn(model.data.firstDay, today)
    val shownRange = if (dayOpened) StatsRange.Day else shownView.coerce(range)

    fun pick(date: LocalDate) {
        pickedDay = if (date == today) FOLLOW_TODAY else date.toEpochDay()
        dayOpened = true
    }

    val own = remember(actions) {
        StatsPaneActions(
            { dayOpened = false; actions.onSelectView(it) }, { dayOpened = false; actions.onSelectRange(it) }, actions.onViewsChange,
            actions.onConnectHealth, actions.onRefreshSleep, actions.onAllowUsage,
        )
    }
    val chips = remember(views, shownView) { if (shownView in views.shown) views.shown else views.shown + shownView }
    LazyColumn(
        modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        item {
            AbPageHeader("Stats") { AbQuietIconButton(Icons.Outlined.Tune, "Choose and order the views", onClick = { sheetOpen = true }) }
        }

        item { StatsViewChips(chips, shownView, onSelect = own.onSelectView) }
        if (shownView.ranges.isNotEmpty() && shownRange != null) {
            item {
                AbSegmented(shownView.ranges.map { it.label }, shownView.ranges.indexOf(shownRange), onSelect = { own.onSelectRange(shownView.ranges[it]) })
            }
        }

        item { StatsViewContent(model, shownView, shownRange, day, own, onPickDay = ::pick) }
    }

    if (sheetOpen) {
        StatsViewsSheet(
            views, onChange = own.onViewsChange,
            onOpen = { opened ->
                own.onSelectView(opened)
                sheetOpen = false
            },
            onDismiss = { sheetOpen = false },
        )
    }
}

private const val FOLLOW_TODAY = Long.MIN_VALUE
