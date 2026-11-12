package com.accountabilibuddy.companion.ui.goals

import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
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
    val minute by produceState(Instant.now()) {
        while (true) {
            val now = Instant.now()
            value = now
            delay(60_000L - (now.toEpochMilli() % 60_000L))
        }
    }
    return produceState<PhoneGoalPage?>(null, goalId, status, lists, display.weekStart, display.extraTiles, allReceived, received, steps, minute) {
        if (status != PhoneGoalStatus.Ready) {
            value = null
            return@produceState
        }
        value = withContext(Dispatchers.Default) {
            runCatching {
                PhoneGoalPage.build(
                    goalId, lists, display, allReceived.map { it.file }, received.map { it.file }, steps, app.sharedLists.allLabels(), Instant.now(), ZoneId.systemDefault(),
                )
            }.getOrNull()
        }
    }
}
