package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
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
import androidx.compose.runtime.saveable.Saver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.StatsFormat
import com.accountabilibuddy.companion.stats.StatsPeriod
import com.accountabilibuddy.companion.stats.StatsPeriods
import com.accountabilibuddy.companion.stats.StatsRange
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.stats.StatsViewsState
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import java.time.LocalDate

/**
 * The Stats tab (boards 5d, 19e to 19h): "Stats" with a tune button, a row of chips for the views, the range (calendar kinds, then
 * Last 7/30/90 and Custom), the period bar (‹ dates ›, with the two dates of a Custom range) and the view itself.
 * It only draws what [model] holds and forwards choices through [actions]; the host owns the model and the saved choices, and nothing
 * here starts, ends or unlocks anything. No Scaffold and no fixed width: the chips ([StatsViewChips]), the sheet ([StatsViewsSheet])
 * and the view's body ([StatsViewContent]) are separate composables a tablet can place in its own columns.
 *
 * @param view The view on show, from the saved choice.
 * @param range The kind of range the person chose, or null for none yet (each view then starts at the one its board shows). The
 * position stepped to with the arrows is held here for the visit only and never saved: a restart opens on the current period.
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
    // The period stepped to; null follows the current one (so it moves on at midnight). Custom is always held as picked.
    var stepped by rememberSaveable(stateSaver = SteppedSaver) { mutableStateOf<StatsPeriod?>(null) }
    // The first day this view has anything for: the ‹ arrow and the Custom pickers go no further back (Steps has its own 30 days).
    val earliest = StatsPeriods.firstReachable(shownView, model.data.firstData, today)
    val period = when (shownRange) {
        null -> null
        StatsRange.Day -> StatsPeriod.of(StatsRange.Day, day)
        else -> StatsPeriods.resolve(shownRange, stepped, today, earliest)
    }
    // A Custom range opens on its dates and keeps them (it does not roll with the date like the current week does).
    LaunchedEffect(shownRange, stepped?.kind) {
        if (shownRange == StatsRange.Custom && stepped?.kind != StatsRange.Custom) stepped = StatsPeriod.current(StatsRange.Custom, today)
    }

    fun pick(date: LocalDate) {
        pickedDay = if (date == today) FOLLOW_TODAY else date.toEpochDay()
        dayOpened = true
    }

    fun setDay(date: LocalDate) {
        pickedDay = if (date == today) FOLLOW_TODAY else date.toEpochDay()
    }

    fun goTo(next: StatsPeriod) {
        if (next.kind == StatsRange.Day) setDay(next.from) else stepped = StatsPeriods.toHold(next, today)
    }

    val underlyingRange = shownView.coerce(range)
    val own = remember(actions, shownRange, underlyingRange, dayOpened, today) {
        StatsPaneActions(
            { dayOpened = false; actions.onSelectView(it) },
            {
                // The chip already chosen jumps back to the current period (Custom keeps its dates); another kind opens on its own. Coming back
                // from a day opened by a tap on a bar to the kind it came from keeps the period that was stepped to.
                val sameKind = it == underlyingRange
                if (!sameKind || (it != StatsRange.Custom && !dayOpened)) stepped = null
                if (it == StatsRange.Custom && stepped == null) stepped = StatsPeriod.current(StatsRange.Custom, today)
                dayOpened = false
                actions.onSelectRange(it)
            },
            actions.onViewsChange,
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
                // Two rows of segments share one choice: the calendar kinds, then the rolling ones and Custom (the other row shows none).
                val calendar = shownView.ranges.filter { !it.isRolling }
                val rolling = shownView.ranges.filter { it.isRolling }
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    AbSegmented(calendar.map { it.label }, calendar.indexOf(shownRange).takeIf { it >= 0 }, onSelect = { own.onSelectRange(calendar[it]) })
                    if (rolling.isNotEmpty()) {
                        AbSegmented(rolling.map { it.label }, rolling.indexOf(shownRange).takeIf { it >= 0 }, onSelect = { own.onSelectRange(rolling[it]) })
                    }
                }
            }
        }

        if (shownView.usesPeriod && period != null) {
            item {
                val isDay = period.kind == StatsRange.Day
                StatsPeriodBar(
                    if (isDay) StatsFormat.dayText(day) else StatsFormat.datesText(period.from, period.to),
                    canStepBack = period.hasPrevious(earliest),
                    canStepForward = period.hasNext(today),
                    onBack = { goTo(period.previous()) },
                    onForward = { goTo(period.next(today)) },
                    onCurrent = { goTo(StatsPeriod.current(period.kind, today)) },
                )
            }

            if (period.kind == StatsRange.Custom) {
                item { StatsCustomDates(period, earliest, today, onChange = { stepped = it }) }
            }
        }

        item { StatsViewContent(model, shownView, shownRange, period, day, own, onPickDay = ::pick) }
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

// The stepped period survives rotation as its kind and two epoch days; it is never written to the preferences.
private val SteppedSaver = Saver<StatsPeriod?, List<Any>>(
    save = { period -> period?.let { listOf(it.kind.name, it.from.toEpochDay(), it.to.toEpochDay()) } ?: emptyList() },
    restore = { saved ->
        runCatching { StatsPeriod(StatsRange.valueOf(saved[0] as String), LocalDate.ofEpochDay(saved[1] as Long), LocalDate.ofEpochDay(saved[2] as Long)) }.getOrNull()
    },
)
