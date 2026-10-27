package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.notify.PinnedTimelineService
import com.accountabilibuddy.companion.placement.PocketPenalty
import kotlinx.coroutines.delay
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import com.accountabilibuddy.companion.usage.PhoneUsageReader

/** What phone placement needs and does, for Settings and the migration assistant. */
@Composable
private fun PlacementText() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val timelineOn = app.store.state.collectAsState().value.timelineNotificationOn
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(
            "During a focus session on your PC the phone tells it where it is (in a bag, face down, face up, in a pocket or in use), " +
                "from its own sensors, so the PC can ask you to put it away. Only that label, times and counts leave the phone, never sensor " +
                "values. Nothing vibrates or sounds. If the phone is out for 3 minutes, the essentials screen opens. During a real session " +
                "with the guard on, unlocking shows a \"Phone down\" screen instead (Putting it away, Need it for work, Essentials / quick " +
                "reply, Emergency); it never covers a call.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        GuardSwitch(app)
        PenaltySwitch(
            app, PocketPenalty.UnlockFriction, "Unlock friction",
            "Just out of a pocket: the Phone down screen's options appear after 10 seconds.",
        )
        PenaltySwitch(
            app, PocketPenalty.NoPhoneFreeCredit, "No phone-free credit for pocket time",
            "More than about 5 minutes in a pocket and the session isn't phone-free (it breaks the streak); the PC's session review shows the pocket minutes.",
        )
        if (!timelineOn) {
            Text(
                "Turn on the timeline notification for phone placement.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.error,
            )
        }
        if (!PhoneUsageReader(context).hasAccess()) {
            Text(
                "Without Usage access the phone can't tell an essential app from another one, so any unlocked use counts as in use.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

/** Settings → Essentials → Phone placement. */
@Composable
internal fun PlacementSection() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val timelineOn = app.store.state.collectAsState().value.timelineNotificationOn
    Section("Phone placement", summary = if (timelineOn) "On during PC sessions" else "Needs the timeline notification") {
        PlacementText()
    }
}

/** The migration assistant's page for the Phone down screen and its pocket penalties. */
@Composable
internal fun PhoneDownSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Phone down screen", style = MaterialTheme.typography.headlineSmall)
        Text(
            "During a real PC focus session with the placement guard on, unlocking the phone shows \"Phones down: in a bag or somewhere off " +
                "you\" instead of the essentials screen. Putting it away closes it; Need it for work, Essentials / quick reply and Emergency " +
                "are the other ways on. It never covers a call. Two pocket penalties are on by default; turning one off waits 10 minutes and " +
                "isn't possible during a session.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        PlacementText()
    }
}

/** The migration assistant's page for phone placement. */
@Composable
internal fun PlacementSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Phone placement", style = MaterialTheme.typography.headlineSmall)
        PlacementText()
    }
}

private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a")

/** The guard's on/off switch: on at once, off waits 10 minutes and is refused during a focus session. */
@Composable
private fun GuardSwitch(app: CompanionApp) {
    val guard = app.placementGuard
    var on by remember { mutableStateOf(guard.isOn(Instant.now())) }
    var offAt by remember { mutableStateOf(guard.pendingOffAt(Instant.now())) }
    var locked by remember { mutableStateOf(guard.locked(Instant.now())) }
    // A pending off comes due, or a session starts, while the page is open.
    LaunchedEffect(Unit) {
        while (true) {
            val now = Instant.now()
            val wasOn = on
            on = guard.isOn(now)
            offAt = guard.pendingOffAt(now)
            locked = guard.locked(now)
            if (wasOn && !on) PinnedTimelineService.checkPlacement()
            delay(2_000L)
        }
    }

    val detail = when {
        locked && on -> "Locked during a focus session"
        offAt != null -> "Turns off at ${CLOCK.format(offAt!!.atZone(ZoneId.systemDefault()))}"
        else -> "Reads the phone's sensors during PC focus sessions. Off: no sensors and the PC never nags about placement."
    }
    ToggleRow("Phone placement guard", detail, on && offAt == null, enabled = !(locked && on)) { wantOn ->
        val now = Instant.now()
        if (wantOn) {
            guard.turnOn()
        } else {
            guard.requestOff(now)
        }

        on = guard.isOn(now)
        offAt = guard.pendingOffAt(now)
        PinnedTimelineService.checkPlacement()
    }
}

/**
 * One pocket penalty's switch (on by default): on applies at once; off waits 10 minutes and is refused during a focus
 * session, like the guard's switch.
 */
@Composable
private fun PenaltySwitch(app: CompanionApp, penalty: PocketPenalty, title: String, description: String) {
    val store = app.phoneDown
    var on by remember { mutableStateOf(store.isOn(penalty, Instant.now())) }
    var offAt by remember { mutableStateOf(store.pendingOffAt(penalty, Instant.now())) }
    var locked by remember { mutableStateOf(store.locked(Instant.now())) }
    // A pending off comes due, or a session starts, while the page is open.
    LaunchedEffect(Unit) {
        while (true) {
            val now = Instant.now()
            on = store.isOn(penalty, now)
            offAt = store.pendingOffAt(penalty, now)
            locked = store.locked(now)
            delay(2_000L)
        }
    }

    val detail = when {
        locked && on -> "Locked during a focus session"
        offAt != null -> "Turns off at ${CLOCK.format(offAt!!.atZone(ZoneId.systemDefault()))}"
        else -> description
    }
    ToggleRow(title, detail, on && offAt == null, enabled = !(locked && on)) { wantOn ->
        val now = Instant.now()
        if (wantOn) {
            store.turnOn(penalty)
        } else {
            store.requestOff(penalty, now)
        }

        on = store.isOn(penalty, now)
        offAt = store.pendingOffAt(penalty, now)
    }
}
