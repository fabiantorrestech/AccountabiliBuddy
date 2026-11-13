package com.accountabilibuddy.companion.ui.stats

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.GoalHeatmapLabels
import com.accountabilibuddy.companion.goals.PhoneGoalData
import com.accountabilibuddy.companion.goals.PhoneGoalStatus
import com.accountabilibuddy.companion.goals.PhoneGoalsOverview
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.goals.GoalFacts
import com.accountabilibuddy.shared.goals.GoalWeekStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.util.UUID

/**
 * What Stats' Goals view draws from (round 2 R6b4): this phone's goals as the Goals tab counts them ([PhoneGoalsOverview]: the same
 * store, received files and steps; nothing new is read, stored or sent) and which goal is on show. Goal names are private text: on
 * screen only.
 *
 * @property overview Null while the numbers are worked out, and while the goals aren't [PhoneGoalStatus.Ready].
 * @property goalId The goal picked (a chip, or the goal Stats was opened for); null or a goal that is gone shows the first one.
 */
class StatsGoalsModel(
    val status: PhoneGoalStatus,
    val overview: PhoneGoalsOverview?,
    val goalId: UUID?,
    val weekStart: GoalWeekStart,
    val labels: GoalHeatmapLabels,
    val monthLines: com.accountabilibuddy.shared.goals.GoalMonthLines,
    val zone: ZoneId,
) {
    /** The goal on show. */
    val shown: GoalFacts?
        get() = overview?.facts?.let { facts -> facts.firstOrNull { it.goal.id == goalId } ?: facts.firstOrNull() }

    /** The day the shown goal began: the ‹ arrow goes no further back. */
    val firstDay: LocalDate? get() = shown?.goal?.createdOn(zone)

    /** The shown goal's area, for the line under its name. */
    fun areaName(facts: GoalFacts): String? = overview?.areas?.firstOrNull { it.id == facts.goal.areaId }?.name
}

/**
 * The goals for Stats, worked out off the main thread and again each minute and whenever the goals, the received files or the steps
 * change (as the Goals tab does). Only composed while the Goals view is on show.
 *
 * @param allReceived Every received device, removed devices' kept copies included (focus, as Stats counts it).
 * @param received The devices still in the group (Deep work's daily target is the newest PC's among them).
 */
@Composable
fun rememberStatsGoals(allReceived: List<ReceivedDevice>, received: List<ReceivedDevice>, goalId: UUID?): StatsGoalsModel {
    val app = LocalContext.current.applicationContext as CompanionApp
    val status by app.goals.status.collectAsStateWithLifecycle()
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val display by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val steps by app.steps.state.collectAsStateWithLifecycle()
    val weeklyState by app.goals.deepWork.state.collectAsStateWithLifecycle()
    // NG1: the phone-owned daily target (no PC in the group) recounts too.
    val dailyState by app.goals.deepWorkDaily.state.collectAsStateWithLifecycle()
    val minute by produceState(Instant.now()) {
        while (true) {
            val now = Instant.now()
            value = now
            delay(60_000L - (now.toEpochMilli() % 60_000L))
        }
    }
    val overview by produceState<PhoneGoalsOverview?>(null, status, lists, weeklyState, dailyState, display.weekStart, allReceived, received, steps, minute) {
        if (status != PhoneGoalStatus.Ready) {
            value = null
            return@produceState
        }
        value = withContext(Dispatchers.Default) {
            runCatching {
                PhoneGoalsOverview.build(
                    lists, display, allReceived.map { it.file }, received.map { it.file }, steps, Instant.now(), ZoneId.systemDefault(),
                    app.goals.deepWork.current(), PhoneGoalData.slept(app),
                )
            }.getOrNull()
        }
    }
    return remember(status, overview, goalId, display.weekStart, display.heatmapLabels, display.monthLines) {
        StatsGoalsModel(status, overview, goalId, display.weekStart, display.heatmapLabels, display.monthLines, ZoneId.systemDefault())
    }
}
