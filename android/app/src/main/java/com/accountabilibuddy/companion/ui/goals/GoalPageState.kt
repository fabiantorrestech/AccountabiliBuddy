package com.accountabilibuddy.companion.ui.goals

import com.accountabilibuddy.shared.tasks.GoalLabelScopes
import com.accountabilibuddy.companion.ui.tasks.scopeDeviceName
import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.PhoneGoalData
import com.accountabilibuddy.companion.goals.PhoneGoalPage
import com.accountabilibuddy.companion.goals.PhoneGoalStatus
import com.accountabilibuddy.companion.sync.ReceivedDevice
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.ZoneId
import java.util.UUID

/**
 * One goal's page numbers ([PhoneGoalPage]), worked out off the main thread and again each minute and whenever the goals, the
 * received files or the steps change. Shared by the goal page and the goal menu so both read the same facts. The last value stays
 * while a new one is worked out (nothing jumps); null when the goals aren't ready or the goal is gone.
 */
@Composable
fun rememberGoalPage(goalId: UUID, allReceived: List<ReceivedDevice>, received: List<ReceivedDevice>): State<PhoneGoalPage?> {
    val app = LocalContext.current.applicationContext as CompanionApp
    val status by app.goals.status.collectAsStateWithLifecycle()
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val display by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val steps by app.steps.state.collectAsStateWithLifecycle()
    // A change of Deep work's weekly target (or its waiting change) recounts.
    val weeklyState by app.goals.deepWork.state.collectAsStateWithLifecycle()
    // NG1: the phone-owned daily target (no PC in the group) recounts too.
    val dailyState by app.goals.deepWorkDaily.state.collectAsStateWithLifecycle()
    val scopeView = com.accountabilibuddy.companion.ui.tasks.rememberScopeView(app)
    val minute by produceState(Instant.now()) {
        while (true) {
            val now = Instant.now()
            value = now
            delay(60_000L - (now.toEpochMilli() % 60_000L))
        }
    }
    return produceState<PhoneGoalPage?>(null, goalId, status, lists, weeklyState, dailyState, display.weekStart, display.extraTiles, allReceived, received, steps, minute, scopeView) {
        if (status != PhoneGoalStatus.Ready) {
            value = null
            return@produceState
        }
        value = withContext(Dispatchers.Default) {
            runCatching {
                // ADR 89: another device's device-only labels aren't named on the page; a source on one reads "Uses a label from <device>".
                val names = app.sharedLists.allLabels()
                PhoneGoalPage.build(
                    goalId, lists, display, allReceived.map { it.file }, received.map { it.file }, steps, GoalLabelScopes.pickable(names, scopeView), Instant.now(), ZoneId.systemDefault(),
                    app.goals.deepWork.current(), PhoneGoalData.slept(app),
                    hiddenNote = { id -> GoalLabelScopes.hiddenNote(id, names, scopeView) { device -> scopeDeviceName(app, device) } },
                )
            }.getOrNull()
        }
    }
}
