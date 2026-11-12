package com.accountabilibuddy.companion.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.SmartBedtime
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.stats.PhoneDay
import com.accountabilibuddy.companion.stats.StatsData
import com.accountabilibuddy.companion.stats.StatsFormat
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.ui.stats.PhoneDayLoader
import com.accountabilibuddy.companion.ui.stats.SleepNightModel
import com.accountabilibuddy.companion.ui.stats.StatsPane
import com.accountabilibuddy.companion.ui.shell.CenteredPane
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.stats.StatsPaneActions
import com.accountabilibuddy.companion.ui.stats.StatsPaneModel
import com.accountabilibuddy.companion.ui.stats.StatsPrefs
import com.accountabilibuddy.companion.ui.stats.rememberStatsGoals
import com.accountabilibuddy.companion.usage.PhoneUsage
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.temporal.ChronoUnit
import java.util.UUID

/**
 * Stats (boards 5d, 19e to 19h): the host of the redesigned pane. It works out what the pane draws from: the sessions and drift the
 * user's devices sent (every received device, removed ones kept, as the PC counts them), this phone's own sleep (Health Connect) and phone
 * use (Android's usage records, read off the main thread). The numbers are looked at again every minute while the app is in front.
 * Only the choice of view, range and chip order is stored (preferences "stats", this phone only); nothing is sent anywhere.
 *
 * @param routeView The part after `stats/` of an `ab.open` route (`stats/consistency`); null or unknown shows the saved view. A goal's
 * id (`stats/<goal id>`, from a goal's page) opens the Goals view on that goal (round 2 R6b4).
 */
@Composable
fun StatsScreen(
    state: CompanionState,
    /** Every device held, kept removed ones included (the sessions); [active] are the ones still in the group (the daily goal). */
    received: List<ReceivedDevice>,
    active: List<ReceivedDevice>,
    health: HealthStatus,
    onConnectHealth: () -> Unit,
    onRefreshSleep: () -> Unit,
    phoneUsage: PhoneUsage?,
    onAllowUsage: () -> Unit,
    routeView: String?,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val prefs = remember { StatsPrefs(context) }
    // The goal the Goals view shows: the one Stats was opened for, then whichever chip is tapped. Held for the visit only, never stored.
    val routeGoal = remember(routeView) { routeView?.takeIf { it.length == GOAL_ID_LENGTH }?.let { runCatching { UUID.fromString(it) }.getOrNull() } }
    var pickedGoal by rememberSaveable { mutableStateOf<String?>(null) }
    LaunchedEffect(routeView) {
        if (routeGoal != null) {
            pickedGoal = routeGoal.toString()
            prefs.select(StatsView.Goals)
        } else {
            StatsView.fromKey(routeView)?.let(prefs::select)
        }
    }

    // Looked at again every minute while the app is in front (and at once when it comes back).
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var now by remember { mutableStateOf(LocalDateTime.now()) }
    LaunchedEffect(lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                now = LocalDateTime.now()
                delay(60_000)
            }
        }
    }

    val zone = ZoneId.systemDefault()
    val plan = state.document.dayPlan
    val minute = now.truncatedTo(ChronoUnit.MINUTES)
    // Worked out off the main thread (it runs every minute and on every sync file); the last result stays on screen while the next builds.
    val built by produceState<StatsData?>(null, received, active, plan, minute) {
        value = withContext(Dispatchers.Default) {
            StatsData.build(received.map { it.file }, active.map { it.file }, Instant.now(), zone) { date -> plan.isWorkday(date) }
        }
    }
    val data = built ?: return

    // This phone's own days, read off the main thread when the focus windows or the minute change (every 5 minutes at most).
    val reader = remember { PhoneUsageReader(context) }
    val usageStep = minute.truncatedTo(ChronoUnit.HOURS).plusMinutes((minute.minute / 5 * 5).toLong())
    val phoneDays by produceState(emptyList<PhoneDay>(), data.windows, phoneUsage != null, usageStep) {
        value = withContext(Dispatchers.IO) { runCatching { PhoneDayLoader.load(reader, data.today, data.windows, zone, Instant.now()) }.getOrDefault(emptyList()) }
    }

    val nights = remember(state.slept, plan, now.toLocalDate(), minute.hour) {
        val store = app.smartBedtime.store
        val known = store.nights().associateBy { it.evening }
        val measured = SmartBedtime.nights(
            state.slept, now, { date: LocalDate -> plan.isWorkdayByWeek(date) },
            phoneDown = { evening, _ -> known[evening]?.phoneDown },
            asked = { evening -> store.asked(evening) },
        )
        (known + measured.associateBy { it.evening }).values.sortedBy { it.evening }
    }

    val sleepNight = remember(state.sleep, plan, state.slept, health, minute.hour) {
        val night = state.sleep.toSchedule(plan).nightAround(now)
        val planned = if (state.sleep.enabled) "Planned: ${StatsFormat.time(night.start)} – ${StatsFormat.time(night.end)} (${StatsFormat.hoursMinutes(Duration.between(night.start, night.end))})" else null
        val last = state.slept.lastOrNull()?.let { "Last sleep: ${StatsFormat.hoursMinutes(Duration.between(it.start, it.end))} (${StatsFormat.time(it.start)} – ${StatsFormat.time(it.end)})" }
        SleepNightModel(planned, health, last)
    }

    // This phone's goals, counted as the Goals tab counts them; only while the Goals view is on show.
    val goals = if (prefs.view == StatsView.Goals) {
        rememberStatsGoals(received, active, pickedGoal?.let { runCatching { UUID.fromString(it) }.getOrNull() })
    } else {
        null
    }

    val model = remember(data, received.isNotEmpty(), phoneDays, phoneUsage, nights, sleepNight, state.sleep, goals) {
        val schedule = state.sleep.toSchedule(plan)
        StatsPaneModel(
            data = data,
            hasDevices = received.isNotEmpty(),
            phoneDays = phoneDays,
            phoneToday = phoneUsage,
            nights = nights,
            sleepGoal = state.sleep.goal,
            sleepNight = sleepNight,
            sleepOn = { date: LocalDate -> if (state.sleep.enabled) listOf<LocalWindow>(schedule.night(date.minusDays(1)), schedule.night(date)) else emptyList() },
            goals = goals,
        )
    }

    val actions = remember(prefs, onConnectHealth, onRefreshSleep, onAllowUsage) {
        StatsPaneActions(prefs::select, prefs::chooseRange, prefs::chooseViews, onConnectHealth, onRefreshSleep, onAllowUsage, onPickGoal = { pickedGoal = it.toString() })
    }
    // On a tablet the charts and tiles keep a readable width in the middle of the pane (a phone draws it as before).
    CenteredPane(maxWidth = 960.dp) { StatsPane(model, prefs.view, prefs.range, prefs.views, actions, modifier) }
}

// A goal's id as a route writes it (a UUID's 36 characters): told apart from a view's key by its length.
private const val GOAL_ID_LENGTH = 36
